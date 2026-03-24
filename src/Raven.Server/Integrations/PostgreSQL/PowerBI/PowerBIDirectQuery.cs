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
            List<string> GroupByCols,
            List<string> OrderByCols,
            List<bool> OrderByDescFlags,
            int Limit);

        private sealed record GroupedAggregateShape(
            string GroupByField,
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
            bool Desc);

        private sealed record GroupedAggregateRqlParts(
            string FromText,
            string WhereText,
            string GroupByField,
            string AggregateFunction,
            string AggregateField,
            string AggregateOutput,
            List<GroupedAggregateOrderByPart> OrderBy,
            int Limit);

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

                if (PowerBIInnerRqlExtractor.TryExtractInnerRqlSpan(sql, out var innerStart, out var innerEnd, out var innerRql) == false)
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

                if (TryExtractGroupedAggregateShape(selectStmt, out var aggregateShape))
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

                if (TryExtractDirectQueryShape(selectStmt, out var shape) == false)
                    return false;

                Documents.Queries.AST.Query q;
                try
                {
                    q = QueryMetadata.ParseQuery(innerRql, QueryType.Select);
                }
                catch
                {
                    return false;
                }

                if (q.From.Alias == null)
                    return false;

                if (selectStmt.WhereClause != null)
                {
                    if (PowerBIOuterWhereTranslator.TryTranslateWhere(selectStmt.WhereClause, outerAlias: "_", innerAlias: q.From.Alias, out var whereExpression) == false)
                        return false;

                    q.Where = q.Where == null
                        ? whereExpression
                        : new BinaryExpression(q.Where, whereExpression, OperatorType.And);
                }

                innerRql = q.ToString();

                var rewrittenRql = RewriteRqlProjection(
                    innerRql,
                    projectionCols: shape.ProjectionCols,
                    shape.OrderByCols,
                    orderByDescFlags: shape.OrderByDescFlags,
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

            static bool TryExtractGroupedAggregateShape(SelectStmt selectStmt, out GroupedAggregateShape shape)
            {
                shape = null;

                if (selectStmt == null)
                    return false;

                // Narrow grouped aggregate wrapper shape (Power BI Desktop):
                // - LIMIT + ORDER BY exist on an outer wrapper select
                // - GROUP BY exists on an inner select
                // - projections and filters may appear across wrapper layers

                if (selectStmt.LimitCount == null || selectStmt.LimitOffset != null)
                    return false;

                if (TryExtractLimit(selectStmt, out var limit) == false)
                    return false;

                if (selectStmt.SortClause == null || selectStmt.SortClause.Count == 0)
                    return false;

                // Extract order-by columns from the outer select. These are underscore-qualified.
                var orderByCols = new List<string>(capacity: selectStmt.SortClause.Count);
                var orderByDescFlags = new List<bool>(capacity: selectStmt.SortClause.Count);
                foreach (var sortNode in selectStmt.SortClause)
                {
                    var sortBy = sortNode?.SortBy;
                    if (sortBy == null)
                        return false;

                    var colRef = TryUnwrapToColumnRef(sortBy.Node);
                    if (colRef == null)
                        return false;

                    if (TryExtractOuterUnderscoreQualifiedColumn(colRef, out var colName) == false)
                        return false;

                    orderByCols.Add(colName);
                    orderByDescFlags.Add(sortBy.SortbyDir == SortByDir.SortbyDesc);
                }

                // Descend through pass-through wrapper layers: FROM ( ... ) "_" until we find the grouped aggregate select.
                if (TryFindInnerGroupedSelect(selectStmt, out var groupedSelect) == false)
                    return false;

                if (TryExtractGroupedAggregateShape(groupedSelect, out var groupByField, out var funcName, out var fieldName, out var outputColumn) == false)
                    return false;

                shape = new GroupedAggregateShape(groupByField, funcName, fieldName, outputColumn, orderByCols, orderByDescFlags, limit);
                return true;

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

                static bool TryExtractGroupedAggregateShape(SelectStmt groupedSelect, out string groupByField, out string functionName, out string fieldName, out string outputColumn)
                {
                    groupByField = null;
                    functionName = null;
                    fieldName = null;
                    outputColumn = null;

                    if (groupedSelect == null)
                        return false;

                    if (groupedSelect.GroupClause is not { Count: 1 } groupClause)
                        return false;

                    var groupByCol = TryUnwrapToColumnRef(groupClause[0]);
                    if (groupByCol == null)
                        return false;

                    groupByField = TryExtractLastIdentifierSegment(groupByCol);
                    if (string.IsNullOrWhiteSpace(groupByField))
                        return false;

                    if (groupedSelect.TargetList == null || groupedSelect.TargetList.Count < 2)
                        return false;

                    var targets = groupedSelect.TargetList;

                    ResTarget groupTarget = null;
                    ResTarget aggTarget = null;
                    ColumnRef unwrappedGroupTarget = null;
                    FuncCall unwrappedAggTarget = null;

                    foreach (var t in targets)
                    {
                        var rt = t?.ResTarget;
                        if (rt == null)
                            return false;

                        var col = TryUnwrapToColumnRef(rt.Val);
                        if (col != null)
                        {
                            groupTarget ??= rt;
                            unwrappedGroupTarget ??= col;
                            continue;
                        }

                        var func = TryUnwrapToFuncCall(rt.Val);
                        if (func != null)
                        {
                            aggTarget ??= rt;
                            unwrappedAggTarget ??= func;
                            continue;
                        }

                        // Ignore other projection kinds.
                    }

                    if (unwrappedGroupTarget == null || unwrappedAggTarget == null || groupTarget == null || aggTarget == null)
                        return false;

                    var projectedGroup = TryExtractLastIdentifierSegment(unwrappedGroupTarget);
                    if (string.IsNullOrWhiteSpace(projectedGroup))
                        return false;

                    outputColumn = aggTarget.Name;
                    if (string.IsNullOrWhiteSpace(outputColumn))
                        return false;

                    functionName = unwrappedAggTarget.Funcname is { Count: > 0 }
                        ? unwrappedAggTarget.Funcname[0].String?.Sval
                        : null;

                    if (string.Equals(functionName, "sum", StringComparison.OrdinalIgnoreCase) == false)
                        return false;

                    if (unwrappedAggTarget.Args is not { Count: 1 } args)
                        return false;

                    var colRef = TryUnwrapToColumnRef(args[0]);
                    if (colRef?.Fields is not { Count: > 0 } fields)
                        return false;

                    fieldName = fields[^1].String?.Sval;
                    if (string.IsNullOrWhiteSpace(fieldName))
                        return false;

                    return true;
                }

                static FuncCall TryUnwrapToFuncCall(Node node)
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

                static ColumnRef TryUnwrapToColumnRef(Node node)
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

                var groupId = FormatRqlIdentifier(shape.GroupByField);
                if (groupId == null)
                    return false;

                var fieldId = FormatRqlIdentifier(shape.FieldName);
                if (fieldId == null)
                    return false;

                var outId = FormatRqlIdentifier(shape.OutputColumn);
                if (outId == null)
                    return false;

                if (TryBuildFromText(q, out var fromText) == false)
                    return false;

                TryBuildWhereText(q, out var whereText);

                if (TryBuildGroupedAggregateOrderBy(shape, groupId, outId, out var orderBy) == false)
                    return false;

                parts = new GroupedAggregateRqlParts(
                    FromText: fromText,
                    WhereText: whereText,
                    GroupByField: groupId,
                    AggregateFunction: "sum",
                    AggregateField: fieldId,
                    AggregateOutput: outId,
                    OrderBy: orderBy,
                    Limit: shape.Limit);
                return true;
            }

            static bool TryBuildGroupedAggregateOrderBy(GroupedAggregateShape shape, string groupId, string outId, out List<GroupedAggregateOrderByPart> orderBy)
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
                        parts.Add(new GroupedAggregateOrderByPart(Kind: GroupedOrderByKind.Output, Desc: desc));
                        continue;
                    }

                    if (string.Equals(c, shape.GroupByField, StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(new GroupedAggregateOrderByPart(Kind: GroupedOrderByKind.GroupKey, Desc: desc));
                        continue;
                    }

                    return false;
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
                sb.Append(parts.GroupByField);

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
                                sb.Append(parts.GroupByField);
                                break;
                            default:
                                return null;
                        }

                        if (ob.Desc)
                            sb.Append(" desc");
                    }
                }

                sb.Append(nl);
                sb.Append("select key() as ");
                sb.Append(parts.GroupByField);
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
                        return false;

                    if (TryExtractOuterUnderscoreQualifiedColumn(colRef, out var colName) == false)
                        return false;

                    cols.Add(colName);
                }

                return true;
            }

            static bool TryExtractInnerGroupBySelect(SelectStmt outerSelectStmt, out SelectStmt groupBySelect)
            {
                groupBySelect = null;

                if (outerSelectStmt?.FromClause is not { Count: 1 })
                    return false;

                var rss = outerSelectStmt.FromClause[0]?.RangeSubselect;
                if (rss == null)
                    return false;

                var aliasName = rss.Alias?.Aliasname;
                if (string.IsNullOrWhiteSpace(aliasName) || string.Equals(aliasName, "_", StringComparison.OrdinalIgnoreCase) == false)
                    return false;

                var current = rss.Subquery?.SelectStmt;
                while (current != null)
                {
                    if (current.GroupClause is { Count: > 0 })
                    {
                        groupBySelect = current;
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

            static bool TryExtractOrderByColumns(SelectStmt selectStmt, out List<string> cols, out List<bool> descFlags)
            {
                cols = null;
                descFlags = null;

                if (selectStmt?.SortClause == null || selectStmt.SortClause.Count == 0)
                    return false;

                cols = new List<string>(capacity: selectStmt.SortClause.Count);
                descFlags = new List<bool>(capacity: selectStmt.SortClause.Count);

                foreach (var sortNode in selectStmt.SortClause)
                {
                    var sortBy = sortNode?.SortBy;
                    if (sortBy == null)
                        return false;

                    var colRef = sortBy.Node?.ColumnRef;
                    if (colRef == null)
                        return false;

                    if (TryExtractOuterUnderscoreQualifiedColumn(colRef, out var colName) == false)
                        return false;

                    if (TryNormalizeOrderByHelperColumn(colName, out var normalized))
                        colName = normalized;

                    cols.Add(colName);
                    descFlags.Add(sortBy.SortbyDir == SortByDir.SortbyDesc);
                }

                return true;
            }

            static bool TryNormalizeOrderByHelperColumn(string colName, out string normalized)
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
                if (int.TryParse(idxSpan, out var idx) == false)
                    return false;

                normalized = "o" + (idx / 2);
                return true;
            }

            static bool TryExtractLimit(SelectStmt selectStmt, out int limit)
            {
                limit = 0;

                if (selectStmt?.LimitCount?.AConst?.Ival == null)
                    return false;

                limit = (int)selectStmt.LimitCount.AConst.Ival.Ival;
                return true;
            }

            static bool TryExtractDirectQueryShape(SelectStmt selectStmt, out DirectQueryShape shape)
            {
                shape = null;

                if (TryExtractOuterProjectedColumns(selectStmt, out var cols) == false)
                    return false;

                if (cols.Count == 0)
                    return false;

                if (TryExtractInnerGroupBySelect(selectStmt, out var groupBySelect) == false)
                    return false;

                if (TryExtractGroupByColumns(groupBySelect, out var groupByCols) == false)
                    return false;

                if (groupByCols.Count == 0)
                    return false;

                if (IsSubsetIgnoreCase(cols, groupByCols) == false)
                    return false;

                if (TryExtractOrderByColumns(selectStmt, out var orderByCols, out var orderDescFlags) == false)
                    return false;

                if (orderByCols.Count == 0)
                    return false;

                // Be tolerant of PowerBI helper columns used only for ORDER BY (e.g. null-order helper aliases).
                if (IsSubsetIgnoreCase(orderByCols, cols) == false && IsSubsetIgnoreCase(orderByCols, groupByCols) == false)
                    return false;

                if (TryExtractLimit(selectStmt, out var limit) == false)
                    return false;

                shape = new DirectQueryShape(cols, groupByCols, orderByCols, orderDescFlags, limit);
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

            static bool IsSubsetIgnoreCase(IReadOnlyList<string> subset, IReadOnlyList<string> superset)
            {
                if (subset == null || superset == null)
                    return false;

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in superset)
                {
                    if (string.IsNullOrWhiteSpace(s))
                        continue;

                    set.Add(s);
                }

                foreach (var s in subset)
                {
                    if (string.IsNullOrWhiteSpace(s))
                        return false;

                    if (set.Contains(s) == false)
                        return false;
                }

                return true;
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

            static string RewriteRqlProjection(string innerRql, IReadOnlyList<string> projectionCols, IReadOnlyList<string> orderByCols, IReadOnlyList<bool> orderByDescFlags, int limit)
            {
                if (string.IsNullOrWhiteSpace(innerRql))
                    return null;

                if (projectionCols == null || projectionCols.Count == 0)
                    return null;

                if (orderByCols == null || orderByCols.Count == 0)
                    return null;

                if (orderByDescFlags == null || orderByDescFlags.Count != orderByCols.Count)
                    return null;

                Documents.Queries.AST.Query q;
                try
                {
                    q = QueryMetadata.ParseQuery(innerRql, QueryType.Select);
                }
                catch
                {
                    return null;
                }

                if (q == null)
                    return null;

                Dictionary<string, string> projectionExprs = null;
                if (q.SelectFunctionBody.FunctionText != null)
                {
                    // Use the already-parsed select function body when available to preserve expressions like `name(e)`.
                    projectionExprs = TryExtractProjectionExpressions(q.SelectFunctionBody.FunctionText.AsSpan());
                }

                var prefixQuery = q.ShallowCopy();
                prefixQuery.IsDistinct = false;
                prefixQuery.Filter = null;
                prefixQuery.FilterLimit = null;
                prefixQuery.OrderBy = null;
                prefixQuery.Select = null;
                prefixQuery.SelectFunctionBody = default;
                prefixQuery.Limit = null;
                prefixQuery.Offset = null;

                var prefix = prefixQuery.ToString();
                if (string.IsNullOrWhiteSpace(prefix))
                    return null;

                prefix = prefix.TrimEnd();
                prefix = prefix.Replace("\r\n", "\n", StringComparison.Ordinal);

                var orderByParts = new List<string>(capacity: orderByCols.Count);
                for (int i = 0; i < orderByCols.Count; i++)
                {
                    var colName = orderByCols[i];
                    if (string.IsNullOrWhiteSpace(colName))
                        return null;

                    if (string.Equals(colName, "json()", StringComparison.OrdinalIgnoreCase))
                    {
                        if (q.From.Alias == null)
                            return null;

                        orderByParts.Add(q.From.Alias + (orderByDescFlags[i] ? " desc" : string.Empty));
                        continue;
                    }

                    if (string.Equals(colName, "id()", StringComparison.OrdinalIgnoreCase))
                    {
                        if (q.From.Alias == null)
                            return null;

                        orderByParts.Add($"id({q.From.Alias})" + (orderByDescFlags[i] ? " desc" : string.Empty));
                        continue;
                    }

                    var id = FormatRqlIdentifier(colName);
                    if (id == null)
                        return null;

                    orderByParts.Add(id + (orderByDescFlags[i] ? " desc" : string.Empty));
                }

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

                    var id = FormatRqlIdentifier(colName);
                    if (id == null)
                        return null;

                    var selectField = FormatRqlObjectFieldIdentifier(colName);
                    if (selectField == null)
                        return null;

                    var expr = id;
                    if (projectionExprs != null && projectionExprs.TryGetValue(colName, out var extracted) && string.IsNullOrWhiteSpace(extracted) == false)
                        expr = extracted;

                    selectParts.Add($"{selectField}: {expr}");
                }

                const string nl = "\n";
                return prefix + nl +
                       $"order by {string.Join(", ", orderByParts)}" + nl +
                       $"select distinct {{ {string.Join(", ", selectParts)} }}" + nl +
                       $"limit 0, {limit}";
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
