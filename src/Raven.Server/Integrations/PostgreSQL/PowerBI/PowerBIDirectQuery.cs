using System;
using System.Collections.Generic;
using System.Text;
using PgSqlParser;
using Raven.Server.Documents;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Queries.AST;
using Raven.Server.Logging;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Sparrow.Logging;
using Sparrow.Server.Logging;
using JsAst = Acornima.Ast;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public sealed class PowerBIDirectQuery : PowerBIRqlQuery
    {
        private static readonly RavenLogger Logger = RavenLogManager.Instance.GetLoggerForServer<PowerBIDirectQuery>();


        public PowerBIDirectQuery(string queryString, int[] parametersDataTypes, DocumentDatabase documentDatabase, Dictionary<string, ReplaceColumnValue> replaces = null, int? limit = null)
            : base(queryString, parametersDataTypes, documentDatabase, replaces, limit)
        {
        }

        protected override bool IncludeDocumentIdColumn => false;

        protected override bool IncludePowerBIJsonColumn => false;

        // Fallback row cap applied when the outer PowerBI wrapper has no explicit LIMIT.
        // Matches PowerBI's own "top N+1" convention: it asks for 1,000,001 rows so it can
        // tell whether a 1,000,000-row result was actually truncated. Keep the two fallback
        // sites in sync via this constant.
        private const int DefaultDirectQueryLimit = 1_000_001;

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

        // Single aggregate extracted from the PowerBI wrapper (e.g. sum("Freight") as "a0").
        // A grouped DirectQuery shape holds a list of these; the canonical SUM-only and COUNT-only
        // shapes produce a list of length 1, while the AVG-style SUM+COUNT shape produces two.
        private sealed record Aggregate(
            string FunctionName,
            string FieldName,
            string OutputColumn);

        private sealed record GroupedAggregateShape(
            List<string> GroupByFields,
            List<Aggregate> Aggregates,
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
            int? AggregateIndex,
            bool Desc);

        private sealed record GroupedAggregateRqlParts(
            string FromText,
            string WhereText,
            List<string> GroupByFields,
            List<Aggregate> Aggregates,
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
            List<Aggregate> Aggregates);

        public static bool TryParse(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            pgQuery = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            try
            {
                var sql = queryText;

                var inner = PowerBIInnerRqlExtractor.TryExtractAndResolve(sql);
                if (inner == null)
                    return false;

                // inner.SanitizedSelectStmt is the wrapper AST with the innermost subquery replaced
                // by `select 1`, produced once by the extractor — no re-parse needed here.
                var selectStmt = inner.SanitizedSelectStmt;
                if (selectStmt == null)
                    return false;

                if (TryNormalizeDirectQueryWrapper(selectStmt, out var wrapper) == false)
                    return false;

                if (TryBuildGroupedAggregateShape(wrapper, out var aggregateShape))
                {
                    string rewritten;
                    try
                    {
                        rewritten = RewriteGroupedAggregateRql(inner.ResolvedQuery, aggregateShape);
                    }
                    catch (Exception e)
                    {
                        if (Logger.IsDebugEnabled)
                            Logger.Debug($"{nameof(PowerBIDirectQuery)}: grouped-aggregate RQL rewrite failed. Reason: {e.Message}");
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(rewritten))
                        return false;

                    if (IsValidRqlSelect(rewritten) == false)
                        return false;

                    pgQuery = new PowerBIDirectQuery(rewritten, parametersDataTypes, documentDatabase, replaces: null, limit: null);
                    return true;
                }

                // Scalar aggregates (no GROUP BY) are not supported in Raven RQL.
                if (wrapper.GroupByColumns is not { Count: > 0 } &&
                    wrapper.Aggregates is { Count: > 0 })
                    return false;

                if (TryBuildDirectQueryShape(wrapper, out var shape) == false)
                    return false;

                var q = inner.ResolvedQuery;

                // Object projections require a from-alias; synthesize one when missing.
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
            catch (Exception e)
            {
                if (Logger.IsDebugEnabled)
                    Logger.Debug($"{nameof(PowerBIDirectQuery)}.{nameof(TryParse)} rejected query: {e.Message}");
                pgQuery = null;
                return false;
            }
        }

        private static bool TryNormalizeDirectQueryWrapper(SelectStmt selectStmt, out NormalizedWrapper wrapper)
        {
            wrapper = null;

            if (selectStmt == null)
                return false;

            // Flat grouped shape: GROUP BY at the outermost level, subquery as FROM (no outer "_" wrapper).
            if (selectStmt.GroupClause is { Count: > 0 })
            {
                int? limitFlat = null;
                if (selectStmt.LimitCount != null)
                {
                    if (TryExtractLimit(selectStmt, out var l) == false)
                        return false;
                    limitFlat = l;
                }

                int? offsetFlat = null;
                if (selectStmt.LimitOffset != null)
                {
                    if (TryExtractOffset(selectStmt, out var o) == false)
                        return false;
                    offsetFlat = o;
                }

                if (TryExtractGroupByColumns(selectStmt, out var groupByColsFlat) == false)
                    return false;

                TryExtractAggregates(selectStmt, out var aggregatesFlat);

                // Flat shape has no outer "_" wrapper, so TryExtractSortClauseToOrderBy's
                // underscore-qualified branch naturally falls through to the last-identifier-segment
                // fallback — the same helper serves both shapes.
                if (TryExtractSortClauseToOrderBy(selectStmt, out var orderByColsFlat, out var orderByDescFlagsFlat) == false)
                    return false;

                wrapper = new NormalizedWrapper(
                    OuterProjectedColumns: new List<string>(groupByColsFlat),
                    GroupByColumns: groupByColsFlat,
                    OrderByColumns: orderByColsFlat,
                    OrderByDescFlags: orderByDescFlagsFlat,
                    OuterWhereClause: selectStmt.WhereClause,
                    Limit: limitFlat,
                    Offset: offsetFlat,
                    Aggregates: aggregatesFlat);
                return true;
            }

            // Standard wrapped shape: GROUP BY in an inner subquery, outer "_" wrapper.
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
                if (TryExtractOffset(selectStmt, out var o) == false)
                    return false;
                offset = o;
            }

            if (TryExtractSortClauseToOrderBy(selectStmt, out var orderByCols, out var orderByDescFlags) == false)
                return false;

            // Best-effort: if the wrapper contains an inner GROUP BY, capture it. Otherwise leave null.
            List<string> groupByCols = null;
            List<Aggregate> aggregates = null;
            if (TryFindInnerGroupedSelect(selectStmt, out var groupedSelect))
            {
                if (TryExtractGroupByColumns(groupedSelect, out groupByCols) == false)
                    return false;

                TryExtractAggregates(groupedSelect, out aggregates);

                // Resolve ORDER BY columns through the wrapper chain back to a business
                // field. A null-order CASE helper is transparently unwrapped to the column
                // it guards; a pass-through alias is resolved to its underlying target.
                if (orderByCols.Count > 0)
                {
                    for (int i = 0; i < orderByCols.Count; i++)
                    {
                        if (TryResolveAliasThroughWrappers(selectStmt, orderByCols[i], out var resolved))
                            orderByCols[i] = resolved;
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
                Aggregates: aggregates);
            return true;
        }

        private static bool TryExtractProjectedColumnsFromAnyWrapperLevel(SelectStmt s, out List<string> cols)
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

        private static bool TryExtractSimpleProjectedColumns(SelectStmt s, out List<string> cols)
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
                    // Tolerate null-order CASE helper columns; reject anything else.
                    if (IsPowerBIOrderHelperAlias(rt.Name) && rt.Val?.CaseExpr != null)
                        continue;
                    return false;
                }

                string colName;
                if (colRef.Fields is { Count: > 1 })
                {
                    if (IsPowerBIOrderHelperAlias(rt.Name))
                        continue;

                    colName = TryExtractLastIdentifierSegment(colRef);
                    if (string.IsNullOrWhiteSpace(colName))
                        return false;

                    if (IsPowerBIOrderHelperAlias(colName))
                        continue;

                    cols.Add(colName);
                    continue;
                }

                colName = TryExtractLastIdentifierSegment(colRef);
                if (string.IsNullOrWhiteSpace(colName))
                    return false;

                if (IsPowerBIOrderHelperAlias(colName))
                    continue;

                cols.Add(colName);
            }

            return cols.Count > 0;
        }

        private static bool TryFindInnerGroupedSelect(SelectStmt outerSelectStmt, out SelectStmt groupedSelect)
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

        // Collects every aggregate FuncCall in the target list; non-FuncCall targets are skipped.
        private static bool TryExtractAggregates(SelectStmt groupedSelect, out List<Aggregate> aggregates)
        {
            aggregates = null;

            if (groupedSelect?.TargetList == null || groupedSelect.TargetList.Count == 0)
                return false;

            var list = new List<Aggregate>();
            foreach (var t in groupedSelect.TargetList)
            {
                var rt = t?.ResTarget;
                if (rt?.Val == null)
                    continue;

                var func = PgSqlAstHelpers.UnwrapThroughHarmlessNodes(rt.Val, static n => n.FuncCall);
                if (func == null)
                    continue;

                var name = func.Funcname is { Count: > 0 }
                    ? func.Funcname[0].String?.Sval
                    : null;

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (func.Args is not { Count: 1 } args)
                    continue;

                var arg = PgSqlAstHelpers.UnwrapThroughHarmlessNodes(args[0], static n => n.ColumnRef);
                if (arg?.Fields is not { Count: > 0 } fields)
                    continue;

                var fieldName = fields[^1].String?.Sval;
                var outputColumn = rt.Name;
                if (string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(outputColumn))
                    continue;

                list.Add(new Aggregate(FunctionName: name, FieldName: fieldName, OutputColumn: outputColumn));
            }

            if (list.Count == 0)
                return false;

            aggregates = list;
            return true;
        }

        private static bool TryBuildGroupedAggregateShape(NormalizedWrapper wrapper, out GroupedAggregateShape shape)
        {
            shape = null;
            if (wrapper == null)
                return false;

            if (wrapper.Aggregates is not { Count: > 0 } aggregates)
                return false;

            foreach (var agg in aggregates)
            {
                if (string.IsNullOrWhiteSpace(agg.FunctionName) ||
                    string.IsNullOrWhiteSpace(agg.FieldName) ||
                    string.IsNullOrWhiteSpace(agg.OutputColumn))
                    return false;

                if (IsSupportedGroupedAggregateFunction(agg.FunctionName) == false)
                    return false;
            }

            if (wrapper.GroupByColumns is not { Count: > 0 })
                return false;

            // Non-zero OFFSET would skip rows; reject. OFFSET 0 is a no-op and is accepted.
            if (wrapper.Offset != null && wrapper.Offset != 0)
                return false;

            if (wrapper.OuterWhereClause != null)
            {
                // Accept if the outer "is not null" guard targets any one of the aggregate outputs.
                var matchedAnyAggregateOutput = false;
                foreach (var agg in aggregates)
                {
                    if (TryIsOuterAggregateNotNullFilter(wrapper.OuterWhereClause, expectedName: agg.OutputColumn))
                    {
                        matchedAnyAggregateOutput = true;
                        break;
                    }
                }

                if (matchedAnyAggregateOutput == false)
                    return false;
            }

            shape = new GroupedAggregateShape(
                GroupByFields: wrapper.GroupByColumns,
                Aggregates: aggregates,
                OrderByCols: wrapper.OrderByColumns,
                OrderByDescFlags: wrapper.OrderByDescFlags,
                Limit: wrapper.Limit ?? DefaultDirectQueryLimit);
            return true;
        }

        private static bool TryIsOuterAggregateNotNullFilter(Node whereClause, string expectedName)
        {
            if (whereClause == null || string.IsNullOrWhiteSpace(expectedName))
                return false;

            if (TryExtractNotNullTest(whereClause, out var colRef) == false)
                return false;

            if (TryExtractOuterUnderscoreQualifiedColumn(colRef, out var col) == false)
                return false;

            return string.Equals(col, expectedName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryExtractNotNullTest(Node where, out ColumnRef colRef)
        {
            colRef = null;

            // Direct: "_"."a0" is not null
            if (where.NullTest != null)
            {
                var nt = where.NullTest;
                if (nt.Nulltesttype == NullTestType.IsNotNull)
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

            if (innerNt.Nulltesttype != NullTestType.IsNull)
                return false;

            colRef = innerNt.Arg?.ColumnRef;
            return colRef != null;
        }

        private static bool TryBuildDirectQueryShape(NormalizedWrapper wrapper, out DirectQueryShape shape)
        {
            shape = null;
            if (wrapper == null)
                return false;

            if (wrapper.OuterProjectedColumns is not { Count: > 0 })
                return false;

            if (wrapper.GroupByColumns is not { Count: > 0 })
                return false;

            var limit = wrapper.Limit ?? DefaultDirectQueryLimit;
            shape = new DirectQueryShape(wrapper.OuterProjectedColumns, limit);
            return true;
        }

        private static string RewriteGroupedAggregateRql(Documents.Queries.AST.Query q, GroupedAggregateShape shape)
        {
            if (TryBuildGroupedAggregateParts(q, shape, out var parts) == false)
                return null;

            return EmitGroupedAggregateRql(parts);
        }

        private static bool TryBuildGroupedAggregateParts(Documents.Queries.AST.Query q, GroupedAggregateShape shape, out GroupedAggregateRqlParts parts)
        {
            parts = null;

            if (q == null || shape == null)
                return false;

            if (q.From.From == null)
                return false;

            if (shape.Aggregates is not { Count: > 0 } aggregates)
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

            var formattedAggregates = new List<Aggregate>(capacity: aggregates.Count);
            foreach (var agg in aggregates)
            {
                if (IsSupportedGroupedAggregateFunction(agg.FunctionName) == false)
                    return false;

                var fieldId = FormatRqlIdentifier(agg.FieldName);
                if (fieldId == null)
                    return false;

                var outId = FormatRqlIdentifier(agg.OutputColumn);
                if (outId == null)
                    return false;

                formattedAggregates.Add(new Aggregate(FunctionName: agg.FunctionName, FieldName: fieldId, OutputColumn: outId));
            }

            if (TryBuildFromText(q, out var fromText) == false)
                return false;

            TryBuildWhereText(q, out var whereText);

            if (TryBuildGroupedAggregateOrderBy(shape, groupIds, out var orderBy) == false)
                return false;

            parts = new GroupedAggregateRqlParts(
                FromText: fromText,
                WhereText: whereText,
                GroupByFields: groupIds,
                Aggregates: formattedAggregates,
                OrderBy: orderBy,
                Limit: shape.Limit);
            return true;
        }

        private static bool TryBuildGroupedAggregateOrderBy(GroupedAggregateShape shape, List<string> groupIds, out List<GroupedAggregateOrderByPart> orderBy)
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

                // Match against any aggregate's output alias (multi-aggregate shapes may expose
                // several sortable columns, e.g. "a0" = sum, "a1" = count).
                var matchedAggregate = false;
                if (shape.Aggregates != null)
                {
                    for (int aIndex = 0; aIndex < shape.Aggregates.Count; aIndex++)
                    {
                        if (string.Equals(c, shape.Aggregates[aIndex].OutputColumn, StringComparison.OrdinalIgnoreCase))
                        {
                            parts.Add(new GroupedAggregateOrderByPart(Kind: GroupedOrderByKind.Output, GroupKeyIndex: null, AggregateIndex: aIndex, Desc: desc));
                            matchedAggregate = true;
                            break;
                        }
                    }
                }

                if (matchedAggregate)
                    continue;

                if (shape.GroupByFields != null)
                {
                    for (int gbIndex = 0; gbIndex < shape.GroupByFields.Count; gbIndex++)
                    {
                        var gb = shape.GroupByFields[gbIndex];
                        if (string.Equals(c, gb, StringComparison.OrdinalIgnoreCase))
                        {
                            parts.Add(new GroupedAggregateOrderByPart(Kind: GroupedOrderByKind.GroupKey, GroupKeyIndex: gbIndex, AggregateIndex: null, Desc: desc));
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

        private static string EmitGroupedAggregateRql(GroupedAggregateRqlParts parts)
        {
            if (parts == null)
                return null;

            if (parts.Aggregates is not { Count: > 0 })
                return null;

            // Build Raven grouped query explicitly (grammar differs from regular select projection).
            // from <collection>
            // group by <field>
            // where <predicate>
            // order by <aggregate alias> as double [desc], <group field> [desc]
            // select key() as <field>, sum(<field>) as <sumOut>, count() as <countOut>
            // limit 0, <limit>

            const string nl = "\n";
            var sb = new StringBuilder();
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
                            var aIndex = ob.AggregateIndex ?? 0;
                            if ((uint)aIndex >= (uint)parts.Aggregates.Count)
                                return null;
                            var targetAgg = parts.Aggregates[aIndex];
                            sb.Append(targetAgg.OutputColumn);
                            // count() returns a long integer; sum() returns a double.
                            sb.Append(IsCountFunction(targetAgg.FunctionName) ? " as long" : " as double");
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

            foreach (var agg in parts.Aggregates)
            {
                sb.Append(", ");
                if (IsCountFunction(agg.FunctionName))
                {

                    sb.Append("count() as ");
                    sb.Append(agg.OutputColumn);
                }
                else
                {
                    sb.Append(agg.FunctionName);
                    sb.Append('(');
                    sb.Append(agg.FieldName);
                    sb.Append(") as ");
                    sb.Append(agg.OutputColumn);
                }
            }

            sb.Append(nl);
            sb.Append("limit 0, ");
            sb.Append(parts.Limit);

            return sb.ToString();
        }

        private static bool TryBuildFromText(Documents.Queries.AST.Query q, out string fromText)
        {
            fromText = null;

            if (q?.From.From == null)
                return false;

            var sb = new StringBuilder();
            var v = new StringQueryVisitor(sb);
            v.VisitFromClause(q.From.From, q.From.Alias, q.From.Filter, q.From.Index);
            fromText = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(fromText) == false;
        }

        private static bool TryBuildWhereText(Documents.Queries.AST.Query q, out string whereText)
        {
            whereText = null;

            if (q?.Where == null)
                return true;

            var sb = new StringBuilder();
            var v = new StringQueryVisitor(sb);
            v.VisitWhereClause(q.Where);
            var rendered = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(rendered))
                return false;

            whereText = rendered;
            return true;
        }

        private static bool TryExtractOuterProjectedColumns(SelectStmt selectStmt, out List<string> cols)
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
                    // Tolerate PowerBI null-order CASE helper columns; reject anything else.
                    if (IsPowerBIOrderHelperAlias(resTarget.Name) && resTarget.Val?.CaseExpr != null)
                        continue;
                    return false;
                }

                if (TryExtractOuterUnderscoreQualifiedColumn(colRef, out var colName) == false)
                {
                    // Qualified ref that doesn't follow the "_"."X" pattern:
                    // skip if it has a helper alias.
                    if (IsPowerBIOrderHelperAlias(resTarget.Name))
                        continue;
                    return false;
                }

                if (IsPowerBIOrderHelperAlias(colName))
                    continue;

                cols.Add(colName);
            }

            return cols.Count > 0;
        }

        // Recognises the two bespoke alias names PowerBI emits purely to drive its own
        // null-order/ordering scaffolding, with no business-field meaning:
        //   - "t<N>_0"  → null-order CASE helper (e.g. "t2_0", "t3_0"); the guarded column
        //                 is resolved structurally via TryExtractNullOrderHelperGuardedColumn.
        //   - "o<N>"    → order-alias passthrough (e.g. "o2", "o3").
        // We drop targets carrying these aliases from the outer projection set so they do
        // not appear as business columns in the emitted RQL.
        private static bool IsPowerBIOrderHelperAlias(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // t<N>_0
            if (name.Length >= 4 && (name[0] == 't' || name[0] == 'T'))
            {
                var u = name.IndexOf('_');
                if (u >= 2 && u < name.Length - 1 &&
                    name.AsSpan(u).Equals("_0", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(name.AsSpan(1, u - 1), out _))
                    return true;
            }

            // o<N>
            if (name.Length >= 2 && (name[0] == 'o' || name[0] == 'O') &&
                int.TryParse(name.AsSpan(1), out _))
                return true;

            return false;
        }

        private static bool TryExtractGroupByColumns(SelectStmt selectStmt, out List<string> cols)
        {
            cols = null;

            if (selectStmt?.GroupClause == null || selectStmt.GroupClause.Count == 0)
                return false;

            cols = new List<string>(capacity: selectStmt.GroupClause.Count);
            foreach (var node in selectStmt.GroupClause)
            {
                // Walk through harmless wrapping nodes (TypeCast, RelabelType) to reach the
                // underlying ColumnRef. PowerBI sometimes emits explicit type coercions in
                // GROUP BY, e.g. "GROUP BY ""Employee""::text". The cast is irrelevant for
                // RQL grouping (Raven groups by field reference, not by typed expression).
                var colRef = PgSqlAstHelpers.UnwrapThroughHarmlessNodes(node, static n => n.ColumnRef);
                if (colRef == null)
                    return false;

                var colName = TryExtractLastIdentifierSegment(colRef);
                if (string.IsNullOrWhiteSpace(colName))
                    return false;

                cols.Add(colName);
            }

            return true;
        }

        // Structurally resolve an outer alias (e.g. an ORDER BY column reference) down through
        // the wrapper chain to the underlying business-field name.
        //
        // At each level we look for a target whose alias matches the current name, then follow
        // its Val:
        //   - ColumnRef          → continue with the ColumnRef's last identifier segment.
        //   - Null-order CASE    → continue with the column guarded by the IS [NOT] NULL test;
        //                          this is how PowerBI's t<N>_0 helpers are transparently
        //                          unwrapped without relying on name conventions.
        //   - anything else      → stop (we cannot express it in RQL).
        //
        // Returns the final resolved name, or the last name seen if we run out of wrapper levels.
        private static bool TryResolveAliasThroughWrappers(SelectStmt outer, string aliasName, out string resolved)
        {
            resolved = aliasName;

            if (outer == null || string.IsNullOrWhiteSpace(aliasName))
                return false;

            var current = outer;
            var currentName = aliasName;

            while (current != null)
            {
                var target = FindTargetByAlias(current, currentName);
                if (target == null)
                {
                    // Not produced here; descend blindly: outer ORDER BY can reference a
                    // column exposed by the wrapper alias without an explicit ResTarget.Name.
                    current = SingleWrapperChild(current);
                    continue;
                }

                var val = target.Val;
                if (val?.ColumnRef != null)
                {
                    var next = TryExtractLastIdentifierSegment(val.ColumnRef);
                    if (string.IsNullOrWhiteSpace(next))
                        return false;
                    currentName = next;
                    resolved = next;
                    current = SingleWrapperChild(current);
                    continue;
                }

                if (val?.CaseExpr != null && TryExtractNullOrderHelperGuardedColumn(val.CaseExpr, out var guarded))
                {
                    currentName = guarded;
                    resolved = guarded;
                    current = SingleWrapperChild(current);
                    continue;
                }

                return false;
            }

            return true;
        }

        private static ResTarget FindTargetByAlias(SelectStmt s, string name)
        {
            if (s?.TargetList == null)
                return null;

            foreach (var t in s.TargetList)
            {
                var rt = t?.ResTarget;
                if (rt == null)
                    continue;

                var rtName = rt.Name;
                if (string.IsNullOrWhiteSpace(rtName))
                    rtName = rt.Val?.ColumnRef is { Fields.Count: > 0 } cr ? cr.Fields[^1]?.String?.Sval : null;

                if (string.Equals(rtName, name, StringComparison.OrdinalIgnoreCase))
                    return rt;
            }

            return null;
        }

        private static SelectStmt SingleWrapperChild(SelectStmt s) =>
            s?.FromClause is { Count: 1 } from
                ? from[0]?.RangeSubselect?.Subquery?.SelectStmt
                : null;

        // PowerBI null-order helper CASE shapes:
        //   (A) case when X is not null then X else <timestamp-literal> end
        //   (B) case when X is [not] null then 0 else 1 end
        // Both are identified by a single WHEN branch whose condition is a NullTest.
        // Returns the guarded column's last identifier segment for shape (A); shape (B) has no
        // useful business-field mapping, so we drop it by returning false.
        private static bool TryExtractNullOrderHelperGuardedColumn(CaseExpr caseExpr, out string columnName)
        {
            columnName = null;

            if (caseExpr?.Args is not { Count: 1 } args)
                return false;

            var when = args[0]?.CaseWhen;
            if (when?.Expr?.NullTest?.Arg?.ColumnRef is not { } guardCol)
                return false;

            // Shape (A): THEN is the same ColumnRef; skip shape (B) which returns 0/1.
            if (when.Result?.ColumnRef == null)
                return false;

            columnName = TryExtractLastIdentifierSegment(guardCol);
            return string.IsNullOrWhiteSpace(columnName) == false;
        }

        private static bool TryExtractLimit(SelectStmt selectStmt, out int limit)
        {
            return PgSqlAstHelpers.TryReadNonNegativeIntConst(selectStmt?.LimitCount, out limit);
        }

        private static bool TryExtractOffset(SelectStmt selectStmt, out int offset)
        {
            return PgSqlAstHelpers.TryReadNonNegativeIntConst(selectStmt?.LimitOffset, out offset);
        }

        private static bool IsValidRqlSelect(string rql)
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

        private static string TryExtractLastIdentifierSegment(ColumnRef colRef)
        {
            if (colRef?.Fields == null || colRef.Fields.Count == 0)
                return null;

            var last = colRef.Fields[^1];
            var name = last?.String?.Sval;
            if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
                name = "id()";
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        private static bool TryExtractOuterUnderscoreQualifiedColumn(ColumnRef colRef, out string colName)
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

        // Resolves each SortClause entry to a (column, desc) pair. Accepts either an
        // outer "_"-qualified reference (wrapped shape) or an unqualified identifier
        // (flat shape) via the last-segment fallback. Returns an empty list when the
        // SELECT has no ORDER BY; returns false only when the clause is present but
        // references something we cannot resolve to a plain column.
        private static bool TryExtractSortClauseToOrderBy(SelectStmt selectStmt, out List<string> cols, out List<bool> descFlags)
        {
            cols = new List<string>();
            descFlags = new List<bool>();

            if (selectStmt?.SortClause is not { Count: > 0 } sort)
                return true;

            cols = new List<string>(capacity: sort.Count);
            descFlags = new List<bool>(capacity: sort.Count);

            foreach (var sortNode in sort)
            {
                var sortBy = sortNode?.SortBy;
                if (sortBy == null)
                    return false;

                var colRef = PgSqlAstHelpers.UnwrapThroughHarmlessNodes(sortBy.Node, static n => n.ColumnRef);
                if (colRef == null)
                    return false;

                if (TryExtractOuterUnderscoreQualifiedColumn(colRef, out var colName) == false)
                {
                    colName = TryExtractLastIdentifierSegment(colRef);
                    if (string.IsNullOrWhiteSpace(colName))
                        return false;
                }

                cols.Add(colName);
                descFlags.Add(sortBy.SortbyDir == SortByDir.SortbyDesc);
            }

            return true;
        }

        private static string RewriteSimpleDirectQueryRql(Documents.Queries.AST.Query q, IReadOnlyList<string> projectionCols, int limit)
        {
            if (q == null)
                return null;

            if (q.From.Alias == null)
                q.From.Alias = "_doc";

            if (projectionCols == null || projectionCols.Count == 0)
                return null;

            // If the inner RQL already produces an object projection (e.g. `select { Name: name(e), Title: e.Title }`)
            // whose top-level keys match the outer wrapper's projected columns, preserve that projection. Rebuilding
            // it as `{ col: alias.col }` would drop computed expressions (including declare-function calls) and
            // silently return empty strings for synthesized fields.
            var preserveInner = false;
            if (TryGetSelectFunctionBodyObjectKeys(q.SelectFunctionBody.FunctionText, out var innerKeys)
                && ProjectionKeysEqual(projectionCols, innerKeys))
            {
                preserveInner = true;
            }

            var core = q.ShallowCopy();
            core.IsDistinct = false;
            core.Filter = null;
            core.FilterLimit = null;
            core.OrderBy = null;
            core.Limit = null;
            core.Offset = null;

            if (preserveInner == false)
            {
                core.Select = null;
                core.SelectFunctionBody = default;
            }

            var prefix = core.ToString();
            if (string.IsNullOrWhiteSpace(prefix))
                return null;

            prefix = prefix.TrimEnd();
            prefix = prefix.Replace("\r\n", "\n", StringComparison.Ordinal);

            const string nl = "\n";
            var sb = new StringBuilder();
            sb.Append(prefix);

            if (preserveInner == false)
            {
                var selectParts = new List<string>(capacity: projectionCols.Count);
                for (int i = 0; i < projectionCols.Count; i++)
                {
                    var colName = projectionCols[i];
                    if (string.IsNullOrWhiteSpace(colName))
                        return null;

                    if (string.Equals(colName, "json()", StringComparison.OrdinalIgnoreCase))
                    {
                        selectParts.Add($"\"json()\": {q.From.Alias}");
                        continue;
                    }

                    if (string.Equals(colName, "id()", StringComparison.OrdinalIgnoreCase))
                    {
                        selectParts.Add($"\"id()\": id({q.From.Alias})");
                        continue;
                    }

                    var selectField = FormatRqlObjectFieldIdentifier(colName);
                    if (selectField == null)
                        return null;

                    var expr = BuildFieldExpression(colName, q.From.Alias?.Value);
                    if (expr == null)
                        return null;

                    selectParts.Add($"{selectField}: {expr}");
                }

                sb.Append(nl);
                sb.Append("select { ");
                sb.Append(string.Join(", ", selectParts));
                sb.Append(" }");
            }

            if (limit >= 0)
            {
                sb.Append(nl);
                sb.Append("limit 0, ");
                sb.Append(limit);
            }

            return sb.ToString();
        }

        // Returns true when the outer projection and the inner object-projection body expose the same
        // set of top-level keys (case-insensitive, ignoring order). This is the only case in which it
        // is safe to hand the inner projection through unchanged — any mismatch means the outer shape
        // exposes columns the inner query does not produce (or vice versa).
        private static bool ProjectionKeysEqual(IReadOnlyList<string> projectionCols, IReadOnlyCollection<string> innerKeys)
        {
            if (projectionCols == null || innerKeys == null)
                return false;

            if (projectionCols.Count != innerKeys.Count)
                return false;

            var innerSet = new HashSet<string>(innerKeys, StringComparer.OrdinalIgnoreCase);
            if (innerSet.Count != innerKeys.Count)
                return false;

            foreach (var c in projectionCols)
            {
                if (string.IsNullOrWhiteSpace(c))
                    return false;
                if (innerSet.Contains(c) == false)
                    return false;
            }

            return true;
        }

        // Reads the top-level keys of an RQL object-projection body via Acornima, the same JS
        // parser Raven uses for projection validation (see QueryMetadata.ValidateScript). The body
        // `{ k1: v1, k2: v2, ... }` is parsed as `return { ... };`, so we expect a single
        // ReturnStatement whose argument is an ObjectExpression. Anything else — spreads, computed
        // keys, non-string-literal keys, parse failures — is rejected so the caller falls back to
        // the safe rewrite path. QueryMetadata.ParseQuery only captures FunctionText; Program is
        // populated later during full metadata construction, which is why we parse here directly.
        private static bool TryGetSelectFunctionBodyObjectKeys(string functionText, out List<string> keys)
        {
            keys = null;

            if (string.IsNullOrWhiteSpace(functionText))
                return false;

            JsAst.Script script;
            try
            {
                // Tolerant = false: the projection body already round-trips through Raven's JS
                // parser upstream, so a syntax error here means the shape really is malformed
                // and we should fall back to the rewrite path rather than silently accept it.
                var parser = new Acornima.Parser(new Acornima.ParserOptions { AllowReturnOutsideFunction = true, Tolerant = false });
                script = parser.ParseScript("return " + functionText);
            }
            catch
            {
                return false;
            }

            if (script?.Body is not { Count: 1 } body)
                return false;

            if (body[0] is not JsAst.ReturnStatement ret || ret.Argument is not JsAst.ObjectExpression obj)
                return false;

            var result = new List<string>(capacity: obj.Properties.Count);
            foreach (var node in obj.Properties)
            {
                if (node is not JsAst.Property { Computed: false } prop)
                    return false;

                string name = prop.Key switch
                {
                    JsAst.Identifier id => id.Name,
                    JsAst.StringLiteral sl => sl.Value,
                    _ => null
                };

                if (string.IsNullOrEmpty(name))
                    return false;

                result.Add(name);
            }

            if (result.Count == 0)
                return false;

            keys = result;
            return true;
        }

        private static string BuildFieldExpression(string fieldName, string fromAlias)
        {
            var id = FormatRqlIdentifier(fieldName);
            if (id == null)
                return null;

            return string.IsNullOrWhiteSpace(fromAlias) ? id : fromAlias + "." + id;
        }

        private static bool IsSupportedGroupedAggregateFunction(string name) =>
            string.Equals(name, "sum", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "count", StringComparison.OrdinalIgnoreCase);

        // Raven grouped RQL uses count() with no argument; emit differently from field-taking functions.
        private static bool IsCountFunction(string name) =>
            string.Equals(name, "count", StringComparison.OrdinalIgnoreCase);

        // Accepts only plain ASCII identifiers (letter/underscore followed by alnum/underscore).
        // Anything requiring quoting is rejected so the caller drops the DirectQuery shape
        // rather than emitting bracket-path expressions we would rather avoid.
        private static string FormatRqlIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return null;

            if (char.IsAsciiLetter(identifier[0]) == false && identifier[0] != '_')
                return null;

            for (int i = 1; i < identifier.Length; i++)
            {
                var c = identifier[i];
                if (char.IsAsciiLetterOrDigit(c) == false && c != '_')
                    return null;
            }

            return identifier;
        }

        private static string FormatRqlObjectFieldIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return null;

            var s = identifier;
            bool plain = char.IsAsciiLetter(s[0]) || s[0] == '_';
            for (int i = 1; i < s.Length && plain; i++)
                plain = char.IsAsciiLetterOrDigit(s[i]) || s[i] == '_';

            if (plain)
                return s;

            // Escape backslashes first so we don't double-escape the \ we prepend to "
            // on the next pass. Without this, an input containing \ could leave a trailing
            // \" that looks like an escaped closing quote to downstream RQL readers.
            var escaped = s
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            return "\"" + escaped + "\"";
        }
    }
}
