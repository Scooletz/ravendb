using System;
using System.Collections.Generic;
using System.Linq;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog
{
    internal static class PgVirtualInterpreter
    {
        public static bool TryExecute(string queryText, VirtualQueryContext ctx, out PgTable result)
        {
            result = null;

            if (SelectStmtShape.TryParseSelectStatements(queryText, out var selects) == false)
                return false;

            if (selects.Count == 1)
                return TryExecuteSingle(selects[0], ctx, out result);

            // Multi-statement batch (e.g. Npgsql's `select version(); select current_setting('max_index_keys')`).
            // Each statement must produce a single-row result; merge the rows column-wise.
            var parts = new List<PgTable>(selects.Count);
            foreach (var s in selects)
            {
                if (TryExecuteSingle(s, ctx, out var sub) == false)
                    return false;
                if (sub.Data.Count != 1)
                    return false;
                parts.Add(sub);
            }

            result = MergeColumnWise(parts);
            return true;
        }

        private static bool TryExecuteSingle(SelectStmt selectStmt, VirtualQueryContext ctx, out PgTable result)
        {
            result = null;

            if (RejectUnsupportedClauses(selectStmt))
                return false;

            if (SelectStmtShape.HasNoFromClause(selectStmt))
                return TryExecuteScalarFunction(selectStmt, out result);

            return TryExecuteTableQuery(selectStmt, ctx, out result);
        }

        private static PgTable MergeColumnWise(IReadOnlyList<PgTable> parts)
        {
            var totalColumns = 0;
            foreach (var p in parts)
                totalColumns += p.Columns.Count;

            var columns = new List<PgColumn>(totalColumns);
            var cells = new ReadOnlyMemory<byte>?[totalColumns];

            short outIndex = 0;
            foreach (var part in parts)
            {
                var srcRow = part.Data[0].ColumnData.Span;
                for (int i = 0; i < part.Columns.Count; i++)
                {
                    var src = part.Columns[i];
                    columns.Add(new PgColumn(src.Name, outIndex, src.PgType, src.FormatCode));
                    cells[outIndex] = srcRow[i];
                    outIndex++;
                }
            }

            return new PgTable
            {
                Columns = columns,
                Data = new List<PgDataRow> { new(cells) }
            };
        }

        private static bool RejectUnsupportedClauses(SelectStmt s)
        {
            if (s.GroupClause is { Count: > 0 })
                return true;
            if (s.DistinctClause is { Count: > 0 })
                return true;
            return false;
        }

        // Scalar-function path: SELECT version(), SELECT current_setting('x'), etc.
        private static bool TryExecuteScalarFunction(SelectStmt s, out PgTable result)
        {
            result = null;

            if (s.TargetList is not { Count: 1 } targetList)
                return false;

            var resTarget = targetList[0]?.ResTarget;
            var funcCall = resTarget?.Val?.FuncCall;
            if (funcCall == null)
                return false;

            if (funcCall.Funcname is not { Count: 1 })
                return false;

            var name = funcCall.Funcname[0].String?.Sval;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (PgVirtualDatabase.TryGetFunction(name, out var function) == false)
                return false;

            var args = new List<object>();
            if (funcCall.Args != null)
            {
                foreach (var arg in funcCall.Args)
                {
                    if (TryReadConstantArg(arg, out var value) == false)
                        return false;
                    args.Add(value);
                }
            }

            if (function.TryEvaluate(args, out var output) == false)
                return false;

            var outputName = string.IsNullOrWhiteSpace(resTarget.Name) == false
                ? resTarget.Name
                : function.ResultColumnName;

            var columns = new List<PgColumn>
            {
                new(outputName, columnIndex: 0, pgType: function.PgType, formatCode: PgFormat.Text),
            };

            var rowData = new ReadOnlyMemory<byte>?[]
            {
                function.PgType.ToBytes(output, PgFormat.Text),
            };

            result = new PgTable
            {
                Columns = columns,
                Data = new List<PgDataRow> { new(rowData) },
            };
            return true;
        }

        private static bool TryReadConstantArg(Node node, out object value)
        {
            value = null;
            var c = node?.AConst;
            if (c == null)
                return false;

            if (c.Sval != null && c.Sval.Sval != null)
            {
                value = c.Sval.Sval;
                return true;
            }

            if (c.Ival != null)
            {
                value = (long)c.Ival.Ival;
                return true;
            }

            return false;
        }

        // FROM-bearing path: build a joined-row stream via JoinExecutor, evaluate WHERE on each row,
        // project each target through ExpressionEvaluator, optionally sort/limit, and serialize the
        // surviving rows into a PgTable.
        private static bool TryExecuteTableQuery(SelectStmt s, VirtualQueryContext ctx, out PgTable result)
        {
            result = null;
            if (TryExecuteAsRows(s, ctx, out var columns, out var rows) == false)
                return false;
            result = BuildPgTable(columns, rows);
            return true;
        }

        // Runs the full pipeline but returns object[] rows + their schema instead of a serialized
        // PgTable. Used both by the top-level table-query path and recursively for sub-FROM bodies.
        private static bool TryExecuteAsRows(SelectStmt s, VirtualQueryContext ctx, out List<ProjectedTarget> columns, out List<object[]> rows)
        {
            columns = null;
            rows = null;

            if (RejectUnsupportedClauses(s))
                return false;

            var executor = new JoinExecutor(ctx, (subselect, alias) => TryResolveSubquery(subselect, alias, ctx));
            if (executor.TryExecute(s.FromClause, out var joinedRows, out var sources) == false)
                return false;

            if (TryBuildProjection(s.TargetList, sources, out var projection) == false)
                return false;

            // Filter (WHERE).
            var filtered = new List<JoinExecutor.JoinedRow>();
            foreach (var jr in joinedRows)
            {
                if (s.WhereClause == null)
                {
                    filtered.Add(jr);
                    continue;
                }
                var scope = jr.ToScope(sources);
                if (ExpressionEvaluator.TryEvaluate(s.WhereClause, scope, out var match) == false)
                    return false;
                if (ExpressionEvaluator.IsTruthy(match))
                    filtered.Add(jr);
            }

            // Sort plan: each entry either reuses a projected column or evaluates an expression
            // against the joined row alongside the projection (kept in a hidden tail of each row).
            if (TryBuildSortPlan(s, projection, out var sortPlan, out var extraSortExpressions) == false)
                return false;

            // Project (real columns first, then any expression-only sort keys appended).
            int totalCells = projection.Count + extraSortExpressions.Count;
            var projectedRows = new List<object[]>(filtered.Count);
            foreach (var jr in filtered)
            {
                var scope = jr.ToScope(sources);
                var cells = new object[totalCells];
                for (int i = 0; i < projection.Count; i++)
                {
                    if (ExpressionEvaluator.TryEvaluate(projection[i].Expression, scope, out var value) == false)
                        return false;
                    cells[i] = value;
                }
                for (int i = 0; i < extraSortExpressions.Count; i++)
                {
                    if (ExpressionEvaluator.TryEvaluate(extraSortExpressions[i], scope, out var sortValue) == false)
                        return false;
                    cells[projection.Count + i] = sortValue;
                }
                projectedRows.Add(cells);
            }

            if (sortPlan.Count > 0)
                projectedRows = SortProjectedRows(projectedRows, sortPlan);

            // Offset / limit.
            int offset = 0;
            int? limit = null;
            if (s.LimitOffset != null)
            {
                if (PgSqlAstHelpers.TryReadNonNegativeIntConst(s.LimitOffset, out offset) == false)
                    return false;
            }
            if (s.LimitCount != null)
            {
                if (PgSqlAstHelpers.TryReadNonNegativeIntConst(s.LimitCount, out var l) == false)
                    return false;
                limit = l;
            }
            if (offset > 0)
                projectedRows = projectedRows.Skip(offset).ToList();
            if (limit.HasValue)
                projectedRows = projectedRows.Take(limit.Value).ToList();

            columns = projection;
            rows = projectedRows;
            return true;
        }

        private static (IReadOnlyList<PgVirtualColumn> Columns, IReadOnlyList<object[]> Rows)? TryResolveSubquery(
            RangeSubselect subselect, string alias, VirtualQueryContext ctx)
        {
            var subStmt = subselect.Subquery?.SelectStmt;
            if (subStmt == null)
                return null;

            // Fast path: subqueries whose inner FROM sources are all always-empty don't need a
            // full pipeline run — only the column schema matters. Preserves the prior behaviour
            // for the PowerBI ReferentialConstraints / FkCentric metadata empty-rowsets.
            if (TryDeriveEmptySubqueryColumns(subStmt, out var emptyColumns))
                return (emptyColumns, Array.Empty<object[]>());

            // Real path: recursively execute the inner SELECT into materialized rows.
            if (TryExecuteAsRows(subStmt, ctx, out var innerColumns, out var innerRows) == false)
                return null;

            var virtualColumns = new List<PgVirtualColumn>(innerColumns.Count);
            foreach (var c in innerColumns)
                virtualColumns.Add(new PgVirtualColumn(c.OutputName, c.PgType, c.FormatCode));
            return (virtualColumns, innerRows);
        }

        private static bool TryDeriveEmptySubqueryColumns(SelectStmt s, out IReadOnlyList<PgVirtualColumn> columns)
        {
            columns = null;

            // Inner sources must exist and be always-empty.
            if (s.FromClause is not { Count: > 0 } from)
                return false;

            var innerSources = new List<(string Alias, PgVirtualTable Table)>();
            foreach (var fromNode in from)
            {
                if (TryCollectInnerEmptySources(fromNode, innerSources) == false)
                    return false;
            }

            if (innerSources.Count == 0)
                return false;
            foreach (var src in innerSources)
            {
                if (src.Table.IsAlwaysEmpty == false)
                    return false;
            }

            if (s.TargetList is not { Count: > 0 } innerTargets)
                return false;

            var derived = new List<PgVirtualColumn>(innerTargets.Count);
            foreach (var target in innerTargets)
            {
                var rt = target?.ResTarget;
                if (rt == null)
                    return false;

                if (TryDeriveColumnFromInner(rt, innerSources, out var col) == false)
                    return false;
                derived.Add(col);
            }

            columns = derived;
            return true;
        }

        private static bool TryCollectInnerEmptySources(Node node, List<(string Alias, PgVirtualTable Table)> sources)
        {
            if (node == null) return false;
            if (node.RangeVar != null)
            {
                var rv = node.RangeVar;
                if (PgVirtualDatabase.TryGetTable(rv.Schemaname, rv.Relname, out var table) == false)
                    return false;
                sources.Add((rv.Alias?.Aliasname ?? rv.Relname, table));
                return true;
            }
            if (node.JoinExpr != null)
            {
                return TryCollectInnerEmptySources(node.JoinExpr.Larg, sources) &&
                       TryCollectInnerEmptySources(node.JoinExpr.Rarg, sources);
            }
            return false;
        }

        private static bool TryDeriveColumnFromInner(ResTarget rt, List<(string Alias, PgVirtualTable Table)> sources, out PgVirtualColumn col)
        {
            col = null;
            var val = rt.Val;
            if (val == null)
                return false;

            var colRef = val.ColumnRef;
            if (colRef?.Fields is { Count: > 0 } fields && fields[^1]?.AStar == null)
            {
                var columnName = fields[^1]?.String?.Sval;
                if (string.IsNullOrWhiteSpace(columnName))
                    return false;
                string qualifier = fields.Count >= 2 ? fields[^2]?.String?.Sval : null;

                foreach (var src in sources)
                {
                    if (qualifier != null && string.Equals(src.Alias, qualifier, StringComparison.OrdinalIgnoreCase) == false)
                        continue;
                    foreach (var c in src.Table.Columns)
                    {
                        if (string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            var outName = string.IsNullOrWhiteSpace(rt.Name) == false ? rt.Name : columnName;
                            col = new PgVirtualColumn(outName, c.PgType, c.FormatCode);
                            return true;
                        }
                    }
                    if (qualifier != null) return false;
                }
                return false;
            }

            // Expression-typed inner column needs an explicit alias.
            if (string.IsNullOrWhiteSpace(rt.Name))
                return false;

            var format = sources[0].Table.Columns.Count > 0 ? sources[0].Table.Columns[0].FormatCode : PgFormat.Text;
            col = new PgVirtualColumn(rt.Name, PgText.Default, format);
            return true;
        }

        // Projection planning
        private sealed record ProjectedTarget(Node Expression, string OutputName, PgType PgType, PgFormat FormatCode);

        private static bool TryBuildProjection(IList<Node> targetList, IReadOnlyList<JoinExecutor.SourceInfo> sources, out List<ProjectedTarget> projection)
        {
            projection = new List<ProjectedTarget>();
            if (targetList is not { Count: > 0 })
                return false;

            foreach (var target in targetList)
            {
                var rt = target?.ResTarget;
                if (rt == null)
                    return false;

                var val = rt.Val;
                if (val == null)
                    return false;

                var colRef = val.ColumnRef;
                if (colRef?.Fields is { Count: > 0 } fields)
                {
                    // Wildcard cases:
                    if (fields.Count == 1 && fields[0]?.AStar != null)
                    {
                        ExpandUnqualifiedWildcard(sources, projection);
                        continue;
                    }
                    if (fields.Count >= 2 && fields[^1]?.AStar != null)
                    {
                        var qualifier = fields[^2]?.String?.Sval;
                        if (ExpandQualifiedWildcard(qualifier, sources, projection) == false)
                            return false;
                        continue;
                    }

                    // Plain column ref.
                    var columnName = fields[^1]?.String?.Sval;
                    if (string.IsNullOrWhiteSpace(columnName))
                        return false;
                    string srcQualifier = fields.Count >= 2 ? fields[^2]?.String?.Sval : null;
                    if (TryResolveColumnType(srcQualifier, columnName, sources, out var pgType, out var format) == false)
                        return false;
                    var outName = string.IsNullOrWhiteSpace(rt.Name) == false ? rt.Name : columnName;
                    projection.Add(new ProjectedTarget(val, outName, pgType, format));
                    continue;
                }

                // Expression target — use the alias if given, otherwise PG's default "?column?".
                var (exprPgType, exprFormat) = InferExpressionType(val, sources);
                var exprName = string.IsNullOrWhiteSpace(rt.Name) == false ? rt.Name : "?column?";
                projection.Add(new ProjectedTarget(val, exprName, exprPgType, exprFormat));
            }

            return projection.Count > 0;
        }

        private static void ExpandUnqualifiedWildcard(IReadOnlyList<JoinExecutor.SourceInfo> sources, List<ProjectedTarget> projection)
        {
            foreach (var src in sources)
            {
                foreach (var c in src.Columns)
                {
                    var colRefNode = new Node { ColumnRef = new ColumnRef { Fields = { new Node { String = new PgSqlParser.String { Sval = src.Alias } }, new Node { String = new PgSqlParser.String { Sval = c.Name } } } } };
                    projection.Add(new ProjectedTarget(colRefNode, c.Name, c.PgType, c.FormatCode));
                }
            }
        }

        private static bool ExpandQualifiedWildcard(string qualifier, IReadOnlyList<JoinExecutor.SourceInfo> sources, List<ProjectedTarget> projection)
        {
            if (string.IsNullOrWhiteSpace(qualifier))
                return false;
            foreach (var src in sources)
            {
                if (string.Equals(src.Alias, qualifier, StringComparison.OrdinalIgnoreCase) == false)
                    continue;
                foreach (var c in src.Columns)
                {
                    var colRefNode = new Node
                    {
                        ColumnRef = new ColumnRef
                        {
                            Fields =
                            {
                                new Node { String = new PgSqlParser.String { Sval = src.Alias } },
                                new Node { String = new PgSqlParser.String { Sval = c.Name } }
                            }
                        }
                    };
                    projection.Add(new ProjectedTarget(colRefNode, c.Name, c.PgType, c.FormatCode));
                }
                return true;
            }
            return false;
        }

        private static bool TryResolveColumnType(string qualifier, string columnName, IReadOnlyList<JoinExecutor.SourceInfo> sources, out PgType pgType, out PgFormat format)
        {
            pgType = null;
            format = PgFormat.Text;

            foreach (var src in sources)
            {
                if (qualifier != null && string.Equals(src.Alias, qualifier, StringComparison.OrdinalIgnoreCase) == false)
                    continue;
                foreach (var c in src.Columns)
                {
                    if (string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        pgType = c.PgType;
                        format = c.FormatCode;
                        return true;
                    }
                }
                if (qualifier != null) return false;
            }
            return false;
        }

        // Heuristic: pick a sensible default PgType for arbitrary expressions. The format code is
        // inherited from the first source (all current catalog sources share a format).
        private static (PgType PgType, PgFormat Format) InferExpressionType(Node expr, IReadOnlyList<JoinExecutor.SourceInfo> sources)
        {
            var format = sources.Count > 0 && sources[0].Columns.Count > 0
                ? sources[0].Columns[0].FormatCode
                : PgFormat.Text;

            // AConst-only expression: derive type from the literal.
            if (expr.AConst != null)
                return (InferConstType(expr.AConst), format);

            // CASE WHEN: walk the WHEN/ELSE results and use the most-specific type.
            if (expr.CaseExpr != null)
                return (InferCaseType(expr.CaseExpr, sources), format);

            return (PgText.Default, format);
        }

        private static PgType InferConstType(A_Const c)
        {
            if (c.Ival != null) return PgInt4.Default;
            if (c.Boolval != null) return PgBool.Default;
            if (c.Fval != null) return PgFloat8.Default;
            if (c.Sval?.Sval is { Length: 1 }) return PgChar.Default;
            return PgText.Default;
        }

        private static PgType InferCaseType(CaseExpr caseExpr, IReadOnlyList<JoinExecutor.SourceInfo> sources)
        {
            // Prefer the first non-null result clause's type.
            if (caseExpr.Args != null)
            {
                foreach (var whenNode in caseExpr.Args)
                {
                    var when = whenNode?.CaseWhen;
                    if (when?.Result?.AConst != null)
                        return InferConstType(when.Result.AConst);
                    if (when?.Result?.ColumnRef?.Fields is { Count: > 0 } fields)
                    {
                        var name = fields[^1]?.String?.Sval;
                        string qual = fields.Count >= 2 ? fields[^2]?.String?.Sval : null;
                        if (name != null && TryResolveColumnType(qual, name, sources, out var pgType, out _))
                            return pgType;
                    }
                }
            }
            if (caseExpr.Defresult?.AConst != null)
                return InferConstType(caseExpr.Defresult.AConst);
            return PgText.Default;
        }

        // ORDER BY plan. Each entry either reuses a projected column (sort key matches an output
        // alias) or is evaluated as an expression against the joined row and appended to a hidden
        // tail of each projected row. `SortPlan` indexes into the full per-row cell array.
        private readonly record struct SortKey(int CellIndex, bool Descending);

        private static bool TryBuildSortPlan(SelectStmt s, List<ProjectedTarget> projection, out List<SortKey> plan, out List<Node> extraExpressions)
        {
            plan = new List<SortKey>();
            extraExpressions = new List<Node>();
            if (s.SortClause is not { Count: > 0 } sort)
                return true;

            foreach (var sortNode in sort)
            {
                var sortBy = sortNode?.SortBy;
                if (sortBy?.Node == null)
                    return false;

                // First check: does the sort key reference a projected output alias?
                int projectedIdx = -1;
                var colRef = sortBy.Node.ColumnRef;
                if (colRef?.Fields is { Count: 1 } fields)
                {
                    var name = fields[0]?.String?.Sval;
                    for (int i = 0; i < projection.Count; i++)
                    {
                        if (string.Equals(projection[i].OutputName, name, StringComparison.OrdinalIgnoreCase))
                        {
                            projectedIdx = i;
                            break;
                        }
                    }
                }

                bool descending = sortBy.SortbyDir == SortByDir.SortbyDesc;
                if (projectedIdx >= 0)
                {
                    plan.Add(new SortKey(projectedIdx, descending));
                }
                else
                {
                    // Evaluate the expression alongside the projection.
                    extraExpressions.Add(sortBy.Node);
                    plan.Add(new SortKey(projection.Count + extraExpressions.Count - 1, descending));
                }
            }
            return true;
        }

        private static List<object[]> SortProjectedRows(List<object[]> rows, List<SortKey> plan)
        {
            rows.Sort((a, b) =>
            {
                foreach (var key in plan)
                {
                    var cmp = CompareCells(a[key.CellIndex], b[key.CellIndex]);
                    if (cmp != 0)
                        return key.Descending ? -cmp : cmp;
                }
                return 0;
            });
            return rows;
        }

        private static int CompareCells(object a, object b)
        {
            if (a is null && b is null) return 0;
            if (a is null) return -1;
            if (b is null) return 1;
            if (a is IComparable ca && a.GetType() == b.GetType())
                return ca.CompareTo(b);
            // Best-effort numeric coercion.
            if (TryNumericCompare(a, b, out var nc))
                return nc;
            return string.CompareOrdinal(a.ToString(), b.ToString());
        }

        private static bool TryNumericCompare(object a, object b, out int result)
        {
            result = 0;
            double da = 0, db = 0;
            if (TryToDouble(a, out da) && TryToDouble(b, out db))
            {
                result = da.CompareTo(db);
                return true;
            }
            return false;
        }

        private static bool TryToDouble(object v, out double d)
        {
            switch (v)
            {
                case double dd: d = dd; return true;
                case float f: d = f; return true;
                case long l: d = l; return true;
                case int i: d = i; return true;
                case short s: d = s; return true;
                default: d = 0; return false;
            }
        }

        private static PgTable BuildPgTable(List<ProjectedTarget> projection, List<object[]> rows)
        {
            var pgColumns = new List<PgColumn>(projection.Count);
            for (int i = 0; i < projection.Count; i++)
            {
                pgColumns.Add(new PgColumn(projection[i].OutputName, columnIndex: (short)i, pgType: projection[i].PgType, formatCode: projection[i].FormatCode));
            }

            var data = new List<PgDataRow>(rows.Count);
            foreach (var row in rows)
            {
                var cells = new ReadOnlyMemory<byte>?[projection.Count];
                for (int i = 0; i < projection.Count; i++)
                {
                    var value = row[i];
                    if (value == null)
                        cells[i] = null;
                    else
                        cells[i] = projection[i].PgType.ToBytes(value, projection[i].FormatCode);
                }
                data.Add(new PgDataRow(cells));
            }
            return new PgTable { Columns = pgColumns, Data = data };
        }
    }
}
