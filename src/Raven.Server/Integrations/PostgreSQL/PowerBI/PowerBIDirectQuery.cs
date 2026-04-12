using System;
using System.Collections.Generic;
using PgSqlParser;
using Raven.Server.Documents;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Queries.AST;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public sealed class PowerBIDirectQuery : PowerBIRqlQuery
    {
        public PowerBIDirectQuery(string queryString, int[] parametersDataTypes, DocumentDatabase documentDatabase, Dictionary<string, ReplaceColumnValue> replaces = null, int? limit = null)
            : base(queryString, parametersDataTypes, documentDatabase, replaces, limit)
        {
        }

        protected override bool IncludeDocumentIdColumn => false;

        protected override bool IncludePowerBIJsonColumn => false;

        protected override DynamicJsonValue BeforeRow(BlittableJsonReaderObject jsonResult, short? jsonIndex)
        {
            return null;
        }

        protected override void AfterRow(BlittableJsonReaderObject jsonResult, ReadOnlyMemory<byte>?[] row, short? jsonIndex)
        {
        }

        private sealed record DirectQueryShape(
            List<string> ProjectionCols,
            int Limit);

        private sealed record GroupedAggregateShape(
            List<string> GroupByFields,
            string FunctionName,
            string FieldName,
            string OutputColumn,
            List<string> OrderByCols,
            List<bool> OrderByDescFlags,
            int Limit);

        private enum GroupedOrderByKind
        {
            Output,
            GroupKey
        }

        private sealed record GroupedAggregateOrderByPart(
            GroupedOrderByKind Kind,
            int? GroupKeyIndex,
            bool Desc);

        private sealed record GroupedAggregateRqlParts(
            string FromText,
            string WhereText,
            List<string> GroupByFields,
            string AggregateFunction,
            string AggregateField,
            string AggregateOutput,
            List<GroupedAggregateOrderByPart> OrderBy,
            int Limit);

        private sealed record NormalizedWrapper(
            List<string> OuterProjectedColumns,
            List<string> GroupByColumns,
            List<string> OrderByColumns,
            List<bool> OrderByDescFlags,
            Node OuterWhereClause,
            int? Limit,
            int? Offset,
            string AggregateFunction,
            string AggregateField,
            string AggregateOutput);

        public static bool TryParse(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            pgQuery = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            try
            {
                // Very conservative handler for the PowerBI DirectQuery wrapper shape.
                // On any mismatch, return false so the other PowerBI parsers can handle it.
                var sql = queryText;

                if (PowerBIInnerRqlExtractor.TryExtractInnerRqlSpan(sql, out var innerStart, out var innerEnd, out var innerRql, out var fromTwoParsersPath) == false)
                    return false;

                // Replace the inner RQL (which PgSqlParser cannot parse) with a trivial SQL subquery.
                // Keep parentheses and surrounding SQL intact.
                var sanitizedSql = sql[..innerStart] + "select 1" + sql[innerEnd..];

                var parseResult = Parser.Parse(sanitizedSql);
                if (parseResult.IsSuccess == false || parseResult.Value == null)
                    return false;

                if (parseResult.Value.Stmts == null || parseResult.Value.Stmts.Count != 1)
                    return false;

                var stmt = parseResult.Value.Stmts[0];
                var selectStmt = stmt?.Stmt?.SelectStmt;
                if (selectStmt == null)
                    return false;

                if (TryNormalizeDirectQueryWrapper(selectStmt, out var wrapper) == false)
                    return false;

                if (TryBuildGroupedAggregateShape(wrapper, out var aggregateShape))
                {
                    string rewritten;
                    try
                    {
                        var innerQuery = QueryMetadata.ParseQuery(innerRql, QueryType.Select);

                        rewritten = RewriteGroupedAggregateRql(innerQuery, aggregateShape);
                    }
                    catch
                    {
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(rewritten))
                        return false;

                    if (IsValidRqlSelect(rewritten) == false)
                        return false;

                    pgQuery = new PowerBIDirectQuery(rewritten, parametersDataTypes, documentDatabase, replaces: null, limit: null);
                    return true;
                }

                // Scalar aggregates (aggregate without group by) are not supported in Raven RQL.
                if (wrapper.GroupByColumns is not { Count: > 0 } &&
                    (string.IsNullOrWhiteSpace(wrapper.AggregateFunction) == false ||
                     string.IsNullOrWhiteSpace(wrapper.AggregateField) == false ||
                     string.IsNullOrWhiteSpace(wrapper.AggregateOutput) == false))
                    return false;

                // Scalar aggregates (sum without group by) are not supported in Raven RQL.
                // Keep DirectQuery ownership restricted to non-aggregate wrappers and grouped-aggregate wrappers only.
                if (TryBuildDirectQueryShape(wrapper, out var shape) == false)
                    return false;

                // Reuse the shared extraction-path-aware resolver: treats the inner text as RQL when
                // the preferred two-parsers path confirmed embedded RQL, and as ambiguous otherwise
                // (tries RQL first, then SQL→RQL translation – the SQL-textbox POC path).
                var q = PowerBIInnerRqlExtractor.TryResolveExtractedInnerTextToRqlQuery(innerRql, fromTwoParsersPath);
                if (q == null)
                    return false;

                // Non-aggregate DirectQuery rewrite always emits `select { ... }` (object projection).
                // Raven requires a from-alias for object projections, so synthesize a stable alias when missing.
                if (q.From.Alias == null)
                    q.From.Alias = "_doc";

                if (selectStmt.WhereClause != null)
                {
                    if (PowerBIOuterWhereTranslator.TryTranslateWhere(selectStmt.WhereClause, outerAlias: "_", innerAlias: q.From.Alias, out var whereExpression) == false)
                        return false;

                    q.Where = q.Where == null
                        ? whereExpression
                        : new BinaryExpression(q.Where, whereExpression, OperatorType.And);
                }

                var rewrittenRql = RewriteSimpleDirectQueryRql(
                    q,
                    projectionCols: shape.ProjectionCols,
                    shape.Limit);
                if (rewrittenRql == null)
                    return false;

                if (IsValidRqlSelect(rewrittenRql) == false)
                    return false;

                pgQuery = new PowerBIDirectQuery(rewrittenRql, parametersDataTypes, documentDatabase, replaces: null, limit: null);
                return true;
            }
            catch
            {
                pgQuery = null;
                return false;
            }

            static bool TryNormalizeDirectQueryWrapper(SelectStmt selectStmt, out NormalizedWrapper wrapper)
            {
                wrapper = null;

                if (selectStmt == null)
                    return false;

                if (TryExtractProjectedColumnsFromAnyWrapperLevel(selectStmt, out var outerCols) == false)
                    return false;

                int? limit = null;
                if (selectStmt.LimitCount != null)
                {
                    if (TryExtractLimit(selectStmt, out var l) == false)
                        return false;
                    limit = l;
                }

                int? offset = null;
                if (selectStmt.LimitOffset != null)
                {
                    if (selectStmt.LimitOffset.AConst?.Ival == null)
                        return false;
                    offset = (int)selectStmt.LimitOffset.AConst.Ival.Ival;
                }

                var orderByCols = new List<string>();
                var orderByDescFlags = new List<bool>();
                if (selectStmt.SortClause is { Count: > 0 })
                {
                    orderByCols = new List<string>(capacity: selectStmt.SortClause.Count);
                    orderByDescFlags = new List<bool>(capacity: selectStmt.SortClause.Count);
                    foreach (var sortNode in selectStmt.SortClause)
                    {
                        var sortBy = sortNode?.SortBy;
                        if (sortBy == null)
                            return false;

                        var colRef = TryUnwrapToColumnRefLocal(sortBy.Node);
                        if (colRef == null)
                            return false;

                        if (TryExtractOuterUnderscoreQualifiedColumn(colRef, out var colName) == false)
                            return false;

                        orderByCols.Add(colName);
                        orderByDescFlags.Add(sortBy.SortbyDir == SortByDir.SortbyDesc);
                    }
                }

                // Best-effort: if the wrapper contains an inner GROUP BY, capture it. Otherwise leave null.
                List<string> groupByCols = null;
                string aggFunc = null;
                string aggField = null;
                string aggOutput = null;
                if (TryFindInnerGroupedSelect(selectStmt, out var groupedSelect))
                {
                    if (TryExtractGroupByColumns(groupedSelect, out groupByCols) == false)
                        return false;

                    TryExtractSingleAggregate(groupedSelect, out aggFunc, out aggField, out aggOutput);

                    // Normalize ORDER BY helper columns now that we have access to the grouped select.
                    if (orderByCols.Count > 0)
                    {
                        for (int i = 0; i < orderByCols.Count; i++)
                        {
                            if (TryNormalizeOrderByHelperColumn(orderByCols[i], groupedSelect, out var normalized))
                                orderByCols[i] = normalized;
                        }
                    }
                }

                wrapper = new NormalizedWrapper(
                    OuterProjectedColumns: outerCols,
                    GroupByColumns: groupByCols,
                    OrderByColumns: orderByCols,
                    OrderByDescFlags: orderByDescFlags,
                    OuterWhereClause: selectStmt.WhereClause,
                    Limit: limit,
                    Offset: offset,
                    AggregateFunction: aggFunc,
                    AggregateField: aggField,
                    AggregateOutput: aggOutput);
                return true;

                static bool TryExtractProjectedColumnsFromAnyWrapperLevel(SelectStmt s, out List<string> cols)
                {
                    cols = null;
                    var current = s;
                    while (current != null)
                    {
                        if (TryExtractOuterProjectedColumns(current, out cols))
                            return true;

                        if (TryExtractSimpleProjectedColumns(current, out cols))
                            return true;

                        if (current.FromClause is not { Count: 1 } from)
                            return false;

                        current = from[0]?.RangeSubselect?.Subquery?.SelectStmt;
                    }

                    return false;
                }

                static bool TryExtractSimpleProjectedColumns(SelectStmt s, out List<string> cols)
                {
                    cols = null;

                    if (s?.TargetList == null || s.TargetList.Count == 0)
                        return false;

                    cols = new List<string>(capacity: s.TargetList.Count);
                    foreach (var t in s.TargetList)
                    {
                        var rt = t?.ResTarget;
                        if (rt == null)
                            return false;

                        var colRef = rt.Val?.ColumnRef;
                        if (colRef == null)
                        {
                            // Non-col-ref expression (e.g. CASE): skip only when BOTH the alias matches
                            // a known helper pattern AND the expression is a CASE expression.
                            // Intentional conservative tolerance for PowerBI null-order CASE helpers;
                            // not a general expression interpreter.
                            if (IsHelperColumnAlias(rt.Name) && rt.Val?.CaseExpr != null)
                                continue;
                            return false;
                        }

                        if (colRef.Fields is not { Count: 1 })
                        {
                            // Multi-field qualified ref (e.g. "_"."t2_0"): skip if known helper alias.
                            if (IsHelperColumnAlias(rt.Name))
                                continue;
                            return false;
                        }

                        var colName = colRef.Fields[0]?.String?.Sval;
                        if (string.IsNullOrWhiteSpace(colName))
                            return false;

                        if (IsHelperColumnAlias(colName))
                            continue;

                        cols.Add(colName);
                    }

                    return cols.Count > 0;
                }

                static ColumnRef TryUnwrapToColumnRefLocal(Node node)
                {
                    while (node != null)
                    {
                        if (node.ColumnRef != null)
                            return node.ColumnRef;

                        if (node.TypeCast != null)
                        {
                            node = node.TypeCast.Arg;
                            continue;
                        }

                        if (node.AExpr != null)
                        {
                            node = node.AExpr.Lexpr ?? node.AExpr.Rexpr;
                            continue;
                        }

                        if (node.RelabelType != null)
                        {
                            node = node.RelabelType.Arg;
                            continue;
                        }

                        break;
                    }

                    return null;
                }

                static FuncCall TryUnwrapToFuncCallLocal(Node node)
                {
                    while (node != null)
                    {
                        if (node.FuncCall != null)
                            return node.FuncCall;

                        if (node.TypeCast != null)
                        {
                            node = node.TypeCast.Arg;
                            continue;
                        }

                        if (node.AExpr != null)
                        {
                            node = node.AExpr.Lexpr ?? node.AExpr.Rexpr;
                            continue;
                        }

                        if (node.RelabelType != null)
                        {
                            node = node.RelabelType.Arg;
                            continue;
                        }

                        break;
                    }

                    return null;
                }

                static bool TryFindInnerGroupedSelect(SelectStmt outerSelectStmt, out SelectStmt groupedSelect)
                {
                    groupedSelect = null;

                    if (outerSelectStmt?.FromClause is not { Count: 1 })
                        return false;

                    var rss = outerSelectStmt.FromClause[0]?.RangeSubselect;
                    if (rss == null)
                        return false;

                    var current = rss.Subquery?.SelectStmt;
                    while (current != null)
                    {
                        if (current.GroupClause is { Count: > 0 })
                        {
                            groupedSelect = current;
                            return true;
                        }

                        if (current.FromClause is not { Count: 1 } currentFrom)
                            return false;

                        var next = currentFrom[0]?.RangeSubselect?.Subquery?.SelectStmt;
                        if (next == null)
                            return false;

                        current = next;
                    }

                    return false;
                }

                static bool TryExtractSingleAggregate(SelectStmt groupedSelect, out string functionName, out string fieldName, out string outputColumn)
                {
                    functionName = null;
                    fieldName = null;
                    outputColumn = null;

                    if (groupedSelect?.TargetList == null || groupedSelect.TargetList.Count == 0)
                        return false;

                    foreach (var t in groupedSelect.TargetList)
                    {
                        var rt = t?.ResTarget;
                        if (rt?.Val == null)
                            continue;

                        var func = TryUnwrapToFuncCallLocal(rt.Val);
                        if (func == null)
                            continue;

                        var name = func.Funcname is { Count: > 0 }
                            ? func.Funcname[0].String?.Sval
                            : null;

                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        if (func.Args is not { Count: 1 } args)
                            continue;

                        var arg = TryUnwrapToColumnRefLocal(args[0]);
                        if (arg?.Fields is not { Count: > 0 } fields)
                            continue;

                        functionName = name;
                        fieldName = fields[^1].String?.Sval;
                        outputColumn = rt.Name;
                        return string.IsNullOrWhiteSpace(fieldName) == false && string.IsNullOrWhiteSpace(outputColumn) == false;
                    }

                    return false;
                }
            }

            static bool TryBuildGroupedAggregateShape(NormalizedWrapper wrapper, out GroupedAggregateShape shape)
            {
                shape = null;
                if (wrapper == null)
                    return false;

                if (string.IsNullOrWhiteSpace(wrapper.AggregateFunction) ||
                    string.IsNullOrWhiteSpace(wrapper.AggregateField) ||
                    string.IsNullOrWhiteSpace(wrapper.AggregateOutput))
                    return false;

                if (string.Equals(wrapper.AggregateFunction, "sum", StringComparison.OrdinalIgnoreCase) == false)
                    return false;

                if (wrapper.GroupByColumns is not { Count: > 0 })
                    return false;

                if (wrapper.Limit == null || wrapper.Offset != null)
                    return false;

                if (wrapper.OuterWhereClause != null)
                {
                    if (TryIsOuterAggregateNotNullFilter(wrapper.OuterWhereClause, expectedName: wrapper.AggregateOutput) == false)
                        return false;
                }

                shape = new GroupedAggregateShape(
                    GroupByFields: wrapper.GroupByColumns,
                    FunctionName: wrapper.AggregateFunction,
                    FieldName: wrapper.AggregateField,
                    OutputColumn: wrapper.AggregateOutput,
                    OrderByCols: wrapper.OrderByColumns,
                    OrderByDescFlags: wrapper.OrderByDescFlags,
                    Limit: wrapper.Limit.Value);
                return true;

                static bool TryIsOuterAggregateNotNullFilter(Node whereClause, string expectedName)
                {
                    if (whereClause == null || string.IsNullOrWhiteSpace(expectedName))
                        return false;

                    if (TryExtractNotNullTest(whereClause, out var colRef) == false)
                        return false;

                    if (TryExtractOuterUnderscoreQualifiedColumn(colRef, out var col) == false)
                        return false;

                    return string.Equals(col, expectedName, StringComparison.OrdinalIgnoreCase);

                    static bool TryExtractNotNullTest(Node where, out ColumnRef colRef)
                    {
                        colRef = null;

                        // Direct: "_"."a0" is not null
                        if (where.NullTest != null)
                        {
                            var nt = where.NullTest;
                            if (IsNotNullTest(nt))
                            {
                                colRef = nt.Arg?.ColumnRef;
                                return colRef != null;
                            }

                            return false;
                        }

                        // NOT( "_"."a0" is null )
                        var be = where.BoolExpr;
                        if (be?.Boolop != BoolExprType.NotExpr || be.Args is not { Count: 1 })
                            return false;

                        var inner = be.Args[0];
                        var innerNt = inner?.NullTest;
                        if (innerNt == null)
                            return false;

                        if (IsNullTest(innerNt) == false)
                            return false;

                        colRef = innerNt.Arg?.ColumnRef;
                        return colRef != null;

                        static bool IsNotNullTest(NullTest nt)
                        {
                            var t = nt.Nulltesttype.ToString();
                            return string.Equals(t, "IsNotNull", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(t, "NulltestIsNotNull", StringComparison.OrdinalIgnoreCase);
                        }

                        static bool IsNullTest(NullTest nt)
                        {
                            var t = nt.Nulltesttype.ToString();
                            return string.Equals(t, "IsNull", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(t, "NulltestIsNull", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }

            static bool TryBuildDirectQueryShape(NormalizedWrapper wrapper, out DirectQueryShape shape)
            {
                shape = null;
                if (wrapper == null)
                    return false;

                if (wrapper.OuterProjectedColumns is not { Count: > 0 })
                    return false;

                // Non-aggregate DirectQuery: only own the Power BI distinct-list wrapper family.
                // For current supported scope, require GROUP BY presence.
                if (wrapper.GroupByColumns is not { Count: > 0 })
                    return false;

                // Non-aggregate DirectQuery wrappers: ignore wrapper GROUP BY / ORDER BY.
                var limit = wrapper.Limit ?? 1000001;
                shape = new DirectQueryShape(wrapper.OuterProjectedColumns, limit);
                return true;
            }

            static string RewriteGroupedAggregateRql(Documents.Queries.AST.Query q, GroupedAggregateShape shape)
            {
                if (TryBuildGroupedAggregateParts(q, shape, out var parts) == false)
                    return null;

                return EmitGroupedAggregateRql(parts);
            }

            static bool TryBuildGroupedAggregateParts(Documents.Queries.AST.Query q, GroupedAggregateShape shape, out GroupedAggregateRqlParts parts)
            {
                parts = null;

                if (q == null || shape == null)
                    return false;

                if (q.From.From == null)
                    return false;

                if (string.Equals(shape.FunctionName, "sum", StringComparison.OrdinalIgnoreCase) == false)
                    return false;

                if (shape.GroupByFields is not { Count: > 0 })
                    return false;

                var groupIds = new List<string>(capacity: shape.GroupByFields.Count);
                foreach (var f in shape.GroupByFields)
                {
                    var groupId = FormatRqlIdentifier(f);
                    if (groupId == null)
                        return false;
                    groupIds.Add(groupId);
                }

                var fieldId = FormatRqlIdentifier(shape.FieldName);
                if (fieldId == null)
                    return false;

                var outId = FormatRqlIdentifier(shape.OutputColumn);
                if (outId == null)
                    return false;

                if (TryBuildFromText(q, out var fromText) == false)
                    return false;

                TryBuildWhereText(q, out var whereText);

                if (TryBuildGroupedAggregateOrderBy(shape, groupIds, outId, out var orderBy) == false)
                    return false;

                parts = new GroupedAggregateRqlParts(
                    FromText: fromText,
                    WhereText: whereText,
                    GroupByFields: groupIds,
                    AggregateFunction: "sum",
                    AggregateField: fieldId,
                    AggregateOutput: outId,
                    OrderBy: orderBy,
                    Limit: shape.Limit);
                return true;
            }

            static bool TryBuildGroupedAggregateOrderBy(GroupedAggregateShape shape, List<string> groupIds, string outId, out List<GroupedAggregateOrderByPart> orderBy)
            {
                orderBy = null;

                if (shape.OrderByCols == null || shape.OrderByDescFlags == null)
                    return false;

                if (shape.OrderByCols.Count != shape.OrderByDescFlags.Count)
                    return false;

                var parts = new List<GroupedAggregateOrderByPart>(capacity: shape.OrderByCols.Count);
                for (int i = 0; i < shape.OrderByCols.Count; i++)
                {
                    var c = shape.OrderByCols[i];
                    var desc = shape.OrderByDescFlags[i];

                    if (string.Equals(c, shape.OutputColumn, StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(new GroupedAggregateOrderByPart(Kind: GroupedOrderByKind.Output, GroupKeyIndex: null, Desc: desc));
                        continue;
                    }

                    if (shape.GroupByFields != null)
                    {
                        for (int gbIndex = 0; gbIndex < shape.GroupByFields.Count; gbIndex++)
                        {
                            var gb = shape.GroupByFields[gbIndex];
                            if (string.Equals(c, gb, StringComparison.OrdinalIgnoreCase))
                            {
                                parts.Add(new GroupedAggregateOrderByPart(Kind: GroupedOrderByKind.GroupKey, GroupKeyIndex: gbIndex, Desc: desc));
                                goto next;
                            }
                        }
                    }

                    return false;
                    next:;
                }

                orderBy = parts;
                return true;
            }

            static string EmitGroupedAggregateRql(GroupedAggregateRqlParts parts)
            {
                if (parts == null)
                    return null;

                // Build Raven grouped query explicitly (grammar differs from regular select projection).
                // from <collection>
                // group by <field>
                // where <predicate>
                // order by <aggregate alias> as double [desc], <group field> [desc]
                // select key() as <field>, sum(<field>) as <output>
                // limit 0, <limit>

                const string nl = "\n";
                var sb = new System.Text.StringBuilder();
                sb.Append(parts.FromText);
                sb.Append(nl);
                sb.Append("group by ");
                sb.Append(string.Join(", ", parts.GroupByFields));

                if (string.IsNullOrWhiteSpace(parts.WhereText) == false)
                {
                    sb.Append(nl);
                    sb.Append(parts.WhereText);
                }

                if (parts.OrderBy is { Count: > 0 })
                {
                    sb.Append(nl);
                    sb.Append("order by ");

                    for (int i = 0; i < parts.OrderBy.Count; i++)
                    {
                        if (i > 0)
                            sb.Append(", ");

                        var ob = parts.OrderBy[i];
                        switch (ob.Kind)
                        {
                            case GroupedOrderByKind.Output:
                                sb.Append(parts.AggregateOutput);
                                sb.Append(" as double");
                                break;
                            case GroupedOrderByKind.GroupKey:
                                if (parts.GroupByFields is not { Count: > 0 })
                                    return null;
                                var keyIndex = ob.GroupKeyIndex ?? 0;
                                if ((uint)keyIndex >= (uint)parts.GroupByFields.Count)
                                    return null;
                                sb.Append(parts.GroupByFields[keyIndex]);
                                break;
                            default:
                                return null;
                        }

                        if (ob.Desc)
                            sb.Append(" desc");
                    }
                }

                sb.Append(nl);
                sb.Append("select ");
                for (int i = 0; i < parts.GroupByFields.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    sb.Append(parts.GroupByFields[i]);
                }
                sb.Append(", ");
                sb.Append(parts.AggregateFunction);
                sb.Append('(');
                sb.Append(parts.AggregateField);
                sb.Append(") as ");
                sb.Append(parts.AggregateOutput);

                sb.Append(nl);
                sb.Append("limit 0, ");
                sb.Append(parts.Limit);

                return sb.ToString();
            }

            static bool TryBuildFromText(Documents.Queries.AST.Query q, out string fromText)
            {
                fromText = null;

                if (q?.From.From == null)
                    return false;

                var sb = new System.Text.StringBuilder();
                var v = new StringQueryVisitor(sb);
                v.VisitFromClause(q.From.From, q.From.Alias, q.From.Filter, q.From.Index);
                fromText = sb.ToString().Trim();
                return string.IsNullOrWhiteSpace(fromText) == false;
            }

            static bool TryBuildWhereText(Documents.Queries.AST.Query q, out string whereText)
            {
                whereText = null;

                if (q?.Where == null)
                    return true;

                var sb = new System.Text.StringBuilder();
                var v = new StringQueryVisitor(sb);
                v.VisitWhereClause(q.Where);
                var rendered = sb.ToString().Trim();
                if (string.IsNullOrWhiteSpace(rendered))
                    return false;

                whereText = rendered;
                return true;
            }
            static bool TryExtractOuterProjectedColumns(SelectStmt selectStmt, out List<string> cols)
            {
                cols = null;

                if (selectStmt?.TargetList == null || selectStmt.TargetList.Count == 0)
                    return false;

                cols = new List<string>(capacity: selectStmt.TargetList.Count);
                foreach (var t in selectStmt.TargetList)
                {
                    var resTarget = t?.ResTarget;
                    if (resTarget == null)
                        return false;

                    var colRef = resTarget.Val?.ColumnRef;
                    if (colRef == null)
                    {
                        // Non-col-ref expression (e.g. CASE): skip only when BOTH the alias matches
                        // a known helper pattern AND the expression is a CASE expression.
                        // Intentional conservative tolerance for PowerBI null-order CASE helpers;
                        // not a general expression interpreter.
                        if (IsHelperColumnAlias(resTarget.Name) && resTarget.Val?.CaseExpr != null)
                            continue;
                        return false;
                    }

                    if (TryExtractOuterUnderscoreQualifiedColumn(colRef, out var colName) == false)
                    {
                        // Qualified ref that doesn't follow the "_"."X" pattern:
                        // skip if it has a helper alias.
                        if (IsHelperColumnAlias(resTarget.Name))
                            continue;
                        return false;
                    }

                    if (IsHelperColumnAlias(colName))
                        continue;

                    cols.Add(colName);
                }

                return cols.Count > 0;
            }

            static bool IsHelperColumnAlias(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return false;

                // t<N>_0 pattern — null-order CASE helper (e.g. "t2_0", "t3_0")
                if (name.Length >= 4 && (name[0] == 't' || name[0] == 'T'))
                {
                    var u = name.IndexOf('_');
                    if (u >= 2 && u < name.Length - 1 &&
                        name.AsSpan(u).Equals("_0", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(name.AsSpan(1, u - 1), out _))
                        return true;
                }

                // o<N> pattern — order alias passthrough (e.g. "o2", "o3")
                if (name.Length >= 2 && (name[0] == 'o' || name[0] == 'O') &&
                    int.TryParse(name.AsSpan(1), out _))
                    return true;

                return false;
            }

            static bool TryExtractGroupByColumns(SelectStmt selectStmt, out List<string> cols)
            {
                cols = null;

                if (selectStmt?.GroupClause == null || selectStmt.GroupClause.Count == 0)
                    return false;

                cols = new List<string>(capacity: selectStmt.GroupClause.Count);
                foreach (var node in selectStmt.GroupClause)
                {
                    var colRef = node?.ColumnRef;
                    if (colRef == null)
                        return false;

                    var colName = TryExtractLastIdentifierSegment(colRef);
                    if (string.IsNullOrWhiteSpace(colName))
                        return false;

                    cols.Add(colName);
                }

                return true;
            }

            static bool TryNormalizeOrderByHelperColumn(string colName, SelectStmt groupBySelect, out string normalized)
            {
                normalized = null;

                if (string.IsNullOrWhiteSpace(colName))
                    return false;

                // PowerBI null-order helper pattern:
                //  - t[even]_0 = CASE WHEN oN IS NOT NULL THEN oN ELSE <sentinel timestamp> END
                //  - t[odd]_0  = CASE WHEN oN IS NULL THEN 0 ELSE 1 END
                // In RQL we can't preserve this pattern exactly without full CASE support.
                // For now, we conservatively collapse t* order-bys back to ordering by the original column (oN),
                // which keeps the query in DirectQuery and returns correct projected columns.
                if (colName.Length < 3 || (colName[0] != 't' && colName[0] != 'T'))
                    return false;

                // Expect: t<idx>_0 (we only accept the PowerBI suffix "_0" here)
                int underscore = colName.IndexOf('_');
                if (underscore < 2)
                    return false;

                if (colName.AsSpan(underscore).Equals("_0", StringComparison.OrdinalIgnoreCase) == false)
                    return false;

                var idxSpan = colName.AsSpan(1, underscore - 1);
                if (int.TryParse(idxSpan, out var helperIdx) == false)
                    return false;

                if (TryMapHelperIndexToInnerOrderAlias(groupBySelect, helperIdx, out var orderAlias) == false)
                    return false;

                // Prefer resolving the wrapper alias (e.g. `o2`) back to the underlying business field (e.g. `RequireAt`).
                // This keeps `orderByCols` comparable to `cols`/`groupByCols` and avoids leaking wrapper-only aliases.
                if (TryResolveWrapperAliasToBusinessField(groupBySelect, orderAlias, out normalized))
                    return true;

                normalized = orderAlias;

                return true;
            }

            static bool TryResolveWrapperAliasToBusinessField(SelectStmt groupBySelect, string orderAlias, out string businessField)
            {
                businessField = null;

                if (groupBySelect == null || string.IsNullOrWhiteSpace(orderAlias))
                    return false;

                var current = groupBySelect;
                while (current != null)
                {
                    // We are looking for a projection like:
                    //   <expr> as "o2"
                    // where <expr> ultimately references a business column name.
                    var targets = current.TargetList;
                    if (targets != null)
                    {
                        foreach (var t in targets)
                        {
                            var rt = t?.ResTarget;
                            if (rt == null)
                                continue;

                            if (string.Equals(rt.Name, orderAlias, StringComparison.OrdinalIgnoreCase) == false)
                                continue;

                            // Common case in BI wrappers: `"RequireAt" as "o2"`.
                            var colRef = rt.Val?.ColumnRef;
                            if (colRef != null)
                            {
                                var colName = TryExtractLastIdentifierSegment(colRef);
                                if (string.IsNullOrWhiteSpace(colName) == false &&
                                    colName.StartsWith("o", StringComparison.OrdinalIgnoreCase) == false &&
                                    string.Equals(colName, "t2_0", StringComparison.OrdinalIgnoreCase) == false)
                                {
                                    businessField = colName;
                                    return true;
                                }
                            }

                            // Another common case: `"_"."o2" as "o2"` (pass-through); keep walking.
                            break;
                        }
                    }

                    if (current.FromClause is not { Count: 1 } from)
                        break;

                    current = from[0]?.RangeSubselect?.Subquery?.SelectStmt;
                }

                return false;
            }

            static bool TryMapHelperIndexToInnerOrderAlias(SelectStmt groupBySelect, int helperIdx, out string normalized)
            {
                normalized = null;

                if (groupBySelect == null)
                    return false;

                if (helperIdx < 0)
                    return false;

                // PowerBI null-order helper columns come in pairs: t<even>_0 and t<odd>_0.
                // Both refer to the same underlying `oN` alias. For odd indices, also try the previous even `o<idx-1>`.
                var directAlias = "o" + helperIdx;
                var pairedAlias = (helperIdx & 1) == 1
                    ? "o" + (helperIdx - 1)
                    : null;
                var desiredHelperPair = (helperIdx / 2) * 2;

                var current = groupBySelect;
                while (current != null)
                {
                    if (TryHasAliasInTargetList(current, directAlias))
                    {
                        normalized = directAlias;
                        return true;
                    }

                    if (pairedAlias != null && TryHasAliasInTargetList(current, pairedAlias))
                    {
                        normalized = pairedAlias;
                        return true;
                    }

                    if (TryFindOrderAliasInTargetList(current, helperIdx, desiredHelperPair, out normalized))
                        return true;

                    if (current.FromClause is not { Count: 1 } from)
                        break;

                    current = from[0]?.RangeSubselect?.Subquery?.SelectStmt;
                }

                return false;

                static bool TryHasAliasInTargetList(SelectStmt s, string alias)
                {
                    if (string.IsNullOrWhiteSpace(alias))
                        return false;

                    var targets = s?.TargetList;
                    if (targets == null || targets.Count == 0)
                        return false;

                    foreach (var t in targets)
                    {
                        var rt = t?.ResTarget;
                        var name = rt?.Name;
                        if (string.Equals(name, alias, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }

                    return false;
                }

                static bool TryFindOrderAliasInTargetList(SelectStmt s, int helperIdx, int desiredHelperPair, out string alias)
                {
                    alias = null;

                    var targets = s?.TargetList;
                    if (targets == null || targets.Count == 0)
                        return false;

                    foreach (var t in targets)
                    {
                        var rt = t?.ResTarget;
                        if (rt == null)
                            continue;

                        var name = rt.Name;
                        if (string.IsNullOrWhiteSpace(name) == false && name.Length > 1 && (name[0] == 'o' || name[0] == 'O'))
                        {
                            if (int.TryParse(name.AsSpan(1), out var orderIdx) == false)
                                continue;

                            // Preferred fallback: if there is an `o<idx>` whose numeric suffix matches the helper index, map to it.
                            // This handles sparse numbering like `o2` with `t2_0/t3_0`.
                            if (orderIdx == helperIdx)
                            {
                                alias = "o" + orderIdx;
                                return true;
                            }

                            // Legacy fallback: old behavior for dense `o0/o1/...` wrappers.
                            if (desiredHelperPair == orderIdx * 2)
                            {
                                alias = "o" + orderIdx;
                                return true;
                            }
                        }
                    }

                    return false;
                }
            }

            static bool TryExtractLimit(SelectStmt selectStmt, out int limit)
            {
                limit = 0;

                if (selectStmt?.LimitCount?.AConst?.Ival == null)
                    return false;

                limit = (int)selectStmt.LimitCount.AConst.Ival.Ival;
                return true;
            }

            static bool IsValidRqlSelect(string rql)
            {
                try
                {
                    QueryMetadata.ParseQuery(rql, QueryType.Select);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            static string TryExtractLastIdentifierSegment(ColumnRef colRef)
            {
                if (colRef?.Fields == null || colRef.Fields.Count == 0)
                    return null;

                var last = colRef.Fields[^1];
                var name = last?.String?.Sval;
                if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
                    name = "id()";
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }

            static bool TryExtractOuterUnderscoreQualifiedColumn(ColumnRef colRef, out string colName)
            {
                colName = null;

                if (colRef?.Fields == null || colRef.Fields.Count < 2)
                    return false;

                var first = colRef.Fields[0]?.String?.Sval;
                if (string.Equals(first, "_", StringComparison.OrdinalIgnoreCase) == false)
                    return false;

                colName = TryExtractLastIdentifierSegment(colRef);
                if (string.Equals(colName, "id", StringComparison.OrdinalIgnoreCase))
                    colName = "id()";
                return string.IsNullOrWhiteSpace(colName) == false;
            }

            static string RewriteSimpleDirectQueryRql(Documents.Queries.AST.Query q, IReadOnlyList<string> projectionCols, int limit)
            {
                if (q == null)
                    return null;

                if (q.From.Alias == null)
                    q.From.Alias = "_doc";

                if (projectionCols == null || projectionCols.Count == 0)
                    return null;

                Dictionary<string, string> projectionExprs = null;
                if (q.SelectFunctionBody.FunctionText != null)
                {
                    // Use the already-parsed select function body when available to preserve expressions like `name(e)`.
                    projectionExprs = TryExtractProjectionExpressions(q.SelectFunctionBody.FunctionText.AsSpan());
                }

                var core = q.ShallowCopy();
                core.IsDistinct = false;
                core.Filter = null;
                core.FilterLimit = null;
                core.OrderBy = null;
                core.Select = null;
                core.SelectFunctionBody = default;
                core.Limit = null;
                core.Offset = null;

                var prefix = core.ToString();
                if (string.IsNullOrWhiteSpace(prefix))
                    return null;

                prefix = prefix.TrimEnd();
                prefix = prefix.Replace("\r\n", "\n", StringComparison.Ordinal);

                var selectParts = new List<string>(capacity: projectionCols.Count);
                for (int i = 0; i < projectionCols.Count; i++)
                {
                    var colName = projectionCols[i];
                    if (string.IsNullOrWhiteSpace(colName))
                        return null;

                    if (string.Equals(colName, "json()", StringComparison.OrdinalIgnoreCase))
                    {
                        if (q.From.Alias == null)
                            return null;

                        selectParts.Add($"\"json()\": {q.From.Alias}");
                        continue;
                    }

                    if (string.Equals(colName, "id()", StringComparison.OrdinalIgnoreCase))
                    {
                        if (q.From.Alias == null)
                            return null;

                        selectParts.Add($"\"id()\": id({q.From.Alias})");
                        continue;
                    }

                    var selectField = FormatRqlObjectFieldIdentifier(colName);
                    if (selectField == null)
                        return null;

                    var expr = BuildFieldExpression(colName, q.From.Alias?.Value);
                    if (expr == null)
                        return null;
                    if (projectionExprs != null && projectionExprs.TryGetValue(colName, out var extracted) && string.IsNullOrWhiteSpace(extracted) == false)
                        expr = extracted;

                    selectParts.Add($"{selectField}: {expr}");
                }


                const string nl = "\n";
                var sb = new System.Text.StringBuilder();
                sb.Append(prefix);
                sb.Append(nl);
                sb.Append("select { ");
                sb.Append(string.Join(", ", selectParts));
                sb.Append(" }");

                if (limit >= 0)
                {
                    sb.Append(nl);
                    sb.Append("limit 0, ");
                    sb.Append(limit);
                }

                return sb.ToString();
            }

            static string BuildFieldExpression(string fieldName, string fromAlias)
            {
                if (string.IsNullOrWhiteSpace(fieldName))
                    return null;

                var id = FormatRqlIdentifier(fieldName);
                if (id == null)
                    return null;

                if (string.IsNullOrWhiteSpace(fromAlias))
                    return id;

                // If it doesn't need escaping, it must be a plain identifier, safe to dot-qualify.
                if (string.Equals(id, fieldName, StringComparison.Ordinal))
                    return fromAlias + "." + id;

                // Escaped identifier: use a bracket access on the alias.
                return fromAlias + id;
            }


            static Dictionary<string, string> TryExtractProjectionExpressions(ReadOnlySpan<char> selectClause)
            {
                // Attempt to extract `<field>: <expr>` pairs from `select { ... }` (or just `{ ... }`) in the original inner RQL.
                // On any mismatch, return null and the caller will fall back to using the column identifier as the expression.

                var idxOpen = -1;

                var idxSelect = selectClause.IndexOf("select", StringComparison.OrdinalIgnoreCase);
                if (idxSelect != -1)
                {
                    var idxBrace = selectClause.Slice(idxSelect).IndexOf('{');
                    if (idxBrace != -1)
                        idxOpen = idxSelect + idxBrace;
                }

                if (idxOpen == -1)
                {
                    var idxReturn = selectClause.IndexOf("return", StringComparison.OrdinalIgnoreCase);
                    if (idxReturn != -1)
                    {
                        var idxBrace = selectClause.Slice(idxReturn).IndexOf('{');
                        if (idxBrace != -1)
                            idxOpen = idxReturn + idxBrace;
                    }
                }

                if (idxOpen == -1)
                    idxOpen = selectClause.IndexOf('{');
                if (idxOpen == -1)
                    return null;

                var idxClose = FindMatchingBrace(selectClause, idxOpen);
                if (idxClose == -1)
                    return null;

                var body = selectClause.Slice(idxOpen + 1, idxClose - idxOpen - 1);
                int pos = 0;

                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                while (pos < body.Length)
                {
                    SkipWs(body, ref pos);
                    if (pos >= body.Length)
                        break;

                    if (body[pos] == ',')
                    {
                        pos++;
                        continue;
                    }

                    if (TryReadFieldName(body, ref pos, out var fieldName) == false)
                        return null;

                    if (string.IsNullOrWhiteSpace(fieldName))
                        return null;

                    SkipWs(body, ref pos);
                    if (pos >= body.Length || body[pos] != ':')
                        return null;

                    pos++; // ':'
                    SkipWs(body, ref pos);

                    var exprStart = pos;
                    if (TryScanExpression(body, ref pos, out var exprEnd) == false)
                        return null;

                    var expr = body.Slice(exprStart, exprEnd - exprStart).ToString().Trim();
                    if (string.IsNullOrWhiteSpace(expr))
                        return null;

                    dict[fieldName] = expr;

                    SkipWs(body, ref pos);
                    if (pos < body.Length && body[pos] == ',')
                        pos++;
                }

                return dict.Count == 0 ? null : dict;

                static int FindMatchingBrace(ReadOnlySpan<char> s, int openIndex)
                {
                    int depth = 0;
                    bool inString = false;
                    char stringQuote = '\0';

                    for (int i = openIndex; i < s.Length; i++)
                    {
                        var ch = s[i];

                        if (inString)
                        {
                            if (ch == '\\')
                            {
                                i++;
                                continue;
                            }

                            if (ch == stringQuote)
                                inString = false;

                            continue;
                        }

                        if (ch == '\'' || ch == '"')
                        {
                            inString = true;
                            stringQuote = ch;
                            continue;
                        }

                        if (ch == '{')
                        {
                            depth++;
                            continue;
                        }

                        if (ch == '}')
                        {
                            depth--;
                            if (depth == 0)
                                return i;
                        }
                    }

                    return -1;
                }

                static void SkipWs(ReadOnlySpan<char> s, ref int i)
                {
                    while (i < s.Length && char.IsWhiteSpace(s[i]))
                        i++;
                }

                static bool TryReadFieldName(ReadOnlySpan<char> s, ref int i, out string field)
                {
                    field = null;
                    if (i >= s.Length)
                        return false;

                    if (s[i] == '"' || s[i] == '\'')
                    {
                        var quote = s[i++];
                        int start = i;
                        while (i < s.Length)
                        {
                            var ch = s[i];
                            if (ch == '\\')
                            {
                                i += 2;
                                continue;
                            }

                            if (ch == quote)
                            {
                                field = s.Slice(start, i - start).ToString();
                                i++;
                                return true;
                            }

                            i++;
                        }

                        return false;
                    }

                    int nameStart = i;
                    while (i < s.Length)
                    {
                        var ch = s[i];
                        if (char.IsWhiteSpace(ch) || ch == ':' || ch == ',')
                            break;
                        i++;
                    }

                    if (i == nameStart)
                        return false;

                    field = s.Slice(nameStart, i - nameStart).ToString();
                    return true;
                }

                static bool TryScanExpression(ReadOnlySpan<char> s, ref int i, out int exprEnd)
                {
                    exprEnd = i;

                    int depth = 0;
                    bool inString = false;
                    char stringQuote = '\0';

                    for (; i < s.Length; i++)
                    {
                        var ch = s[i];

                        if (inString)
                        {
                            if (ch == '\\')
                            {
                                i++;
                                continue;
                            }

                            if (ch == stringQuote)
                                inString = false;

                            continue;
                        }

                        if (ch == '\'' || ch == '"')
                        {
                            inString = true;
                            stringQuote = ch;
                            continue;
                        }

                        if (ch is '(' or '[' or '{')
                        {
                            depth++;
                            continue;
                        }

                        if (ch is ')' or ']' or '}')
                        {
                            if (depth > 0)
                            {
                                depth--;
                                continue;
                            }
                        }

                        if (depth == 0 && ch == ',')
                        {
                            exprEnd = i;
                            return true;
                        }
                    }

                    exprEnd = s.Length;
                    return true;
                }
            }

            static string FormatRqlIdentifier(string identifier)
            {
                if (string.IsNullOrWhiteSpace(identifier))
                    return null;

                bool IsAsciiLetter(char ch) => (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
                bool IsAsciiDigit(char ch) => ch >= '0' && ch <= '9';
                bool IsStart(char ch) => IsAsciiLetter(ch) || ch == '_';
                bool IsPart(char ch) => IsStart(ch) || IsAsciiDigit(ch);

                if (IsStart(identifier[0]) == false)
                    return Escape(identifier);

                for (int i = 1; i < identifier.Length; i++)
                {
                    if (IsPart(identifier[i]) == false)
                        return Escape(identifier);
                }

                return identifier;

                static string Escape(string raw)
                {
                    var escaped = raw.Replace("\"", "\\\"", StringComparison.Ordinal);
                    return $"[\"{escaped}\"]";
                }
            }

            static string FormatRqlObjectFieldIdentifier(string identifier)
            {
                if (string.IsNullOrWhiteSpace(identifier))
                    return null;

                bool IsAsciiLetter(char ch) => (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
                bool IsAsciiDigit(char ch) => ch >= '0' && ch <= '9';
                bool IsStart(char ch) => IsAsciiLetter(ch) || ch == '_';
                bool IsPart(char ch) => IsStart(ch) || IsAsciiDigit(ch);

                if (IsStart(identifier[0]) == false)
                    return Quote(identifier);

                for (int i = 1; i < identifier.Length; i++)
                {
                    if (IsPart(identifier[i]) == false)
                        return Quote(identifier);
                }

                return identifier;

                static string Quote(string raw)
                {
                    var escaped = raw.Replace("\"", "\\\"", StringComparison.Ordinal);
                    return "\"" + escaped + "\"";
                }
            }
        }
    }
}
