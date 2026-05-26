using System;
using System.Collections.Generic;
using System.Linq;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Classification;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Translation;
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

        // Table path: SELECT ... FROM <schema>.<table>[, ... | JOIN ...] [WHERE/ORDER/LIMIT].
        // Single-source: full pipeline (WHERE eval / ORDER BY / LIMIT / projection).
        // Multi-source: only supported when at least one source is IsAlwaysEmpty — the join
        // result is then empty by construction, so we just emit the projection's column schema.
        private static bool TryExecuteTableQuery(SelectStmt s, VirtualQueryContext ctx, out PgTable result)
        {
            result = null;

            if (TryCollectFromTables(s, out var sources) == false || sources.Count == 0)
                return false;

            if (sources.Count > 1)
                return TryExecuteEmptyJoin(s, sources, ctx, out result);

            return TryExecuteSingleTable(s, sources[0].Table, ctx, out result);
        }

        private static bool TryExecuteSingleTable(SelectStmt s, PgVirtualTable table, VirtualQueryContext ctx, out PgTable result)
        {
            result = null;

            if (TryBuildProjectionPlan(s, table, out var projection) == false)
                return false;

            ParsedWhere parsedWhere = null;
            if (s.WhereClause != null)
            {
                if (SqlWhereParser.TryParse(s.WhereClause, outerAliasToStrip: null, out parsedWhere) == false)
                    return false;
            }

            if (TryReadSortPlan(s, table, out var sortPlan) == false)
                return false;

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

            var rows = new List<object[]>();
            foreach (var row in table.EnumerateRows(ctx))
            {
                if (parsedWhere != null)
                {
                    if (PredicateEvaluator.TryEvaluate(parsedWhere, table.Columns, row, out var match) == false)
                        return false;
                    if (match == false)
                        continue;
                }
                rows.Add(row);
            }

            if (sortPlan.Count > 0)
                rows = SortRows(rows, table.Columns, sortPlan);

            if (offset > 0)
                rows = rows.Skip(offset).ToList();
            if (limit.HasValue)
                rows = rows.Take(limit.Value).ToList();

            result = BuildPgTable(projection, table, rows);
            return true;
        }

        private static bool TryExecuteEmptyJoin(SelectStmt s, List<SourceTable> sources, VirtualQueryContext ctx, out PgTable result)
        {
            result = null;

            // Empty-shortcut: at least one joined table must be always-empty (which makes the join
            // result empty under inner-join semantics regardless of the ON conditions).
            var anyEmpty = false;
            foreach (var src in sources)
            {
                if (src.Table.IsAlwaysEmpty)
                {
                    anyEmpty = true;
                    break;
                }
            }
            if (anyEmpty == false)
                return false;

            if (TryBuildEmptyJoinProjection(s, sources, out var columns) == false)
                return false;

            result = new PgTable { Columns = columns, Data = new List<PgDataRow>() };
            return true;
        }

        // FROM-clause walker
        private readonly record struct SourceTable(PgVirtualTable Table, string Alias);

        private static bool TryCollectFromTables(SelectStmt s, out List<SourceTable> sources)
        {
            sources = new List<SourceTable>();
            if (s.FromClause == null || s.FromClause.Count == 0)
                return false;

            foreach (var fromNode in s.FromClause)
            {
                if (TryAddFromNode(fromNode, sources) == false)
                    return false;
            }
            return true;
        }

        private static bool TryAddFromNode(Node node, List<SourceTable> sources)
        {
            if (node == null)
                return false;

            if (node.RangeVar != null)
            {
                var rv = node.RangeVar;
                if (PgVirtualDatabase.TryGetTable(rv.Schemaname, rv.Relname, out var table) == false)
                    return false;
                var alias = rv.Alias?.Aliasname ?? rv.Relname;
                sources.Add(new SourceTable(table, alias));
                return true;
            }

            if (node.JoinExpr != null)
            {
                if (TryAddFromNode(node.JoinExpr.Larg, sources) == false)
                    return false;
                if (TryAddFromNode(node.JoinExpr.Rarg, sources) == false)
                    return false;
                return true;
            }

            // RangeSubselect / other constructs: out of scope for now.
            return false;
        }

        // Empty-join projection: derive only the column schema (no row data needed).
        private static bool TryBuildEmptyJoinProjection(SelectStmt s, List<SourceTable> sources, out List<PgColumn> columns)
        {
            columns = new List<PgColumn>();
            if (s.TargetList is not { Count: > 0 } targetList)
                return false;

            short index = 0;
            foreach (var target in targetList)
            {
                var rt = target?.ResTarget;
                if (rt == null)
                    return false;

                if (TryDeriveProjectedColumn(rt, sources, index, out var col) == false)
                    return false;

                columns.Add(col);
                index++;
            }
            return columns.Count > 0;
        }

        private static bool TryDeriveProjectedColumn(ResTarget rt, List<SourceTable> sources, short index, out PgColumn col)
        {
            col = null;
            var val = rt.Val;
            if (val == null)
                return false;

            // Simple column reference: `alias.column` or `column`.
            var colRef = val.ColumnRef;
            if (colRef?.Fields is { Count: > 0 } fields && fields[^1]?.AStar == null)
            {
                var columnName = fields[^1]?.String?.Sval;
                if (string.IsNullOrWhiteSpace(columnName))
                    return false;

                string qualifier = null;
                if (fields.Count >= 2)
                    qualifier = fields[^2]?.String?.Sval;

                if (TryResolveSourceColumn(sources, qualifier, columnName, out var sourceCol) == false)
                    return false;

                var outputName = string.IsNullOrWhiteSpace(rt.Name) == false ? rt.Name : columnName;
                col = new PgColumn(outputName, index, sourceCol.PgType, sourceCol.FormatCode);
                return true;
            }

            // Computed expression (string concat `||`, CASE WHEN, function call, constant, etc.).
            // We don't evaluate it because the result is empty by construction; we just need the
            // column metadata. Default: PgText, format inherited from the first source (all our
            // current empty-join sources share the same format code).
            var outputNameForExpr = string.IsNullOrWhiteSpace(rt.Name) == false ? rt.Name : "?column?";
            var format = sources.Count > 0 && sources[0].Table.Columns.Count > 0
                ? sources[0].Table.Columns[0].FormatCode
                : PgFormat.Text;
            col = new PgColumn(outputNameForExpr, index, PgText.Default, format);
            return true;
        }

        private static bool TryResolveSourceColumn(List<SourceTable> sources, string qualifier, string columnName, out PgVirtualColumn sourceCol)
        {
            sourceCol = null;

            if (string.IsNullOrWhiteSpace(qualifier) == false)
            {
                foreach (var src in sources)
                {
                    if (string.Equals(src.Alias, qualifier, StringComparison.OrdinalIgnoreCase) == false)
                        continue;
                    foreach (var c in src.Table.Columns)
                    {
                        if (string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            sourceCol = c;
                            return true;
                        }
                    }
                    return false;
                }
                return false;
            }

            foreach (var src in sources)
            {
                foreach (var c in src.Table.Columns)
                {
                    if (string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        sourceCol = c;
                        return true;
                    }
                }
            }
            return false;
        }

        // Projection planning
        private readonly record struct ProjectedColumn(int SourceIndex, string OutputName);

        private static bool TryBuildProjectionPlan(SelectStmt s, PgVirtualTable table, out List<ProjectedColumn> plan)
        {
            plan = new List<ProjectedColumn>();

            if (s.TargetList is not { Count: > 0 } targetList)
                return false;

            // Wildcard: SELECT * FROM ...
            if (SelectStmtShape.HasWildcardTarget(s))
            {
                if (targetList.Count != 1)
                    return false;

                for (int i = 0; i < table.Columns.Count; i++)
                    plan.Add(new ProjectedColumn(i, table.Columns[i].Name));
                return true;
            }

            foreach (var target in targetList)
            {
                var rt = target?.ResTarget;
                if (rt == null)
                    return false;

                var colRef = rt.Val?.ColumnRef;
                if (colRef?.Fields is not { Count: > 0 } fields)
                    return false;

                var lastField = fields[^1];
                var columnName = lastField?.String?.Sval;
                if (string.IsNullOrWhiteSpace(columnName))
                    return false;

                var sourceIndex = FindColumnIndex(table.Columns, columnName);
                if (sourceIndex < 0)
                    return false;

                var outputName = string.IsNullOrWhiteSpace(rt.Name) == false ? rt.Name : columnName;
                plan.Add(new ProjectedColumn(sourceIndex, outputName));
            }

            return plan.Count > 0;
        }

        private static int FindColumnIndex(IReadOnlyList<PgVirtualColumn> columns, string name)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        // ORDER BY
        private readonly record struct SortKey(int ColumnIndex, bool Descending);

        private static bool TryReadSortPlan(SelectStmt s, PgVirtualTable table, out List<SortKey> plan)
        {
            plan = new List<SortKey>();
            if (s.SortClause is not { Count: > 0 } sort)
                return true;

            foreach (var sortNode in sort)
            {
                var sortBy = sortNode?.SortBy;
                if (sortBy == null)
                    return false;

                var colRef = sortBy.Node?.ColumnRef;
                if (colRef?.Fields is not { Count: > 0 } fields)
                    return false;

                var name = fields[^1]?.String?.Sval;
                if (string.IsNullOrWhiteSpace(name))
                    return false;

                var idx = FindColumnIndex(table.Columns, name);
                if (idx < 0)
                    return false;

                plan.Add(new SortKey(idx, sortBy.SortbyDir == SortByDir.SortbyDesc));
            }

            return true;
        }

        private static List<object[]> SortRows(List<object[]> rows, IReadOnlyList<PgVirtualColumn> columns, List<SortKey> plan)
        {
            rows.Sort((a, b) =>
            {
                foreach (var key in plan)
                {
                    var cmp = CompareCells(a[key.ColumnIndex], b[key.ColumnIndex]);
                    if (cmp != 0)
                        return key.Descending ? -cmp : cmp;
                }
                return 0;
            });
            return rows;
        }

        private static int CompareCells(object a, object b)
        {
            if (a is null && b is null)
                return 0;
            if (a is null)
                return -1;
            if (b is null)
                return 1;

            if (a is IComparable ca && a.GetType() == b.GetType())
                return ca.CompareTo(b);

            return string.CompareOrdinal(a.ToString(), b.ToString());
        }

        // PgTable assembly
        private static PgTable BuildPgTable(List<ProjectedColumn> plan, PgVirtualTable table, List<object[]> rows)
        {
            var pgColumns = new List<PgColumn>(plan.Count);
            for (int i = 0; i < plan.Count; i++)
            {
                var src = table.Columns[plan[i].SourceIndex];
                pgColumns.Add(new PgColumn(plan[i].OutputName, columnIndex: (short)i, pgType: src.PgType, formatCode: src.FormatCode));
            }

            var data = new List<PgDataRow>(rows.Count);
            foreach (var row in rows)
            {
                var cells = new ReadOnlyMemory<byte>?[plan.Count];
                for (int i = 0; i < plan.Count; i++)
                {
                    var src = table.Columns[plan[i].SourceIndex];
                    var cellValue = row[plan[i].SourceIndex];
                    cells[i] = cellValue == null
                        ? null
                        : src.PgType.ToBytes(cellValue, src.FormatCode);
                }
                data.Add(new PgDataRow(cells));
            }

            return new PgTable { Columns = pgColumns, Data = data };
        }
    }
}
