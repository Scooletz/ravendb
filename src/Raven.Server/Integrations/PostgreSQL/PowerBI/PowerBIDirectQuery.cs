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

        // PowerBI's "top 1,000,000 + 1" convention when the wrapper has no LIMIT.
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

                // Object projections require a from-alias.
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

            // Flat grouped shape: GROUP BY at outermost level, no outer "_" wrapper.
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

            List<string> groupByCols = null;
            List<Aggregate> aggregates = null;
            if (TryFindInnerGroupedSelect(selectStmt, out var groupedSelect))
            {
                if (TryExtractGroupByColumns(groupedSelect, out groupByCols) == false)
                    return false;

                TryExtractAggregates(groupedSelect, out aggregates);

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
                    // Accept PowerBI null-order CASE helpers; reject everything else.
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

            if (wrapper.Offset != null && wrapper.Offset != 0)
                return false;

            if (wrapper.OuterWhereClause != null)
            {
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

                // Match against any aggregate output alias (e.g. "a0" = sum, "a1" = count).
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
                    // Accept PowerBI null-order CASE helpers; reject everything else.
                    if (IsPowerBIOrderHelperAlias(resTarget.Name) && resTarget.Val?.CaseExpr != null)
                        continue;
                    return false;
                }

                if (TryExtractOuterUnderscoreQualifiedColumn(colRef, out var colName) == false)
                {
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

        // PowerBI order-helper aliases — "t<N>_0" (null-order CASE helpers) and "o<N>" (order passthroughs).
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
                // Unwrap ::text / RelabelType casts PowerBI sometimes adds.
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

        // Matches `case when X is [not] null then X else <literal> end` and returns X's last segment.
        private static bool TryExtractNullOrderHelperGuardedColumn(CaseExpr caseExpr, out string columnName)
        {
            columnName = null;

            if (caseExpr?.Args is not { Count: 1 } args)
                return false;

            var when = args[0]?.CaseWhen;
            if (when?.Expr?.NullTest?.Arg?.ColumnRef is not { } guardCol)
                return false;

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

        // Accepts outer "_"-qualified (wrapped shape) or unqualified (flat shape). Empty SortClause → true.
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

        private enum InnerProjectionMode
        {
            Rebuild,
            PreserveExact,
            PreserveWithExtras
        }

        private static string RewriteSimpleDirectQueryRql(Documents.Queries.AST.Query q, IReadOnlyList<string> projectionCols, int limit)
        {
            if (q == null)
                return null;

            if (q.From.Alias == null)
                q.From.Alias = "_doc";

            if (projectionCols == null || projectionCols.Count == 0)
                return null;

            // Preserve the inner object projection when its keys match the outer columns —
            // rebuilding would drop computed expressions (declare-function calls, concats).
            var mode = InnerProjectionMode.Rebuild;
            List<string> extras = null;
            if (TryGetSelectFunctionBodyObjectKeys(q.SelectFunctionBody.FunctionText, out var innerKeys))
                mode = ClassifyInnerProjection(projectionCols, innerKeys, out extras);

            var core = q.ShallowCopy();
            core.IsDistinct = false;
            core.Filter = null;
            core.FilterLimit = null;
            core.OrderBy = null;
            core.Limit = null;
            core.Offset = null;

            if (mode == InnerProjectionMode.Rebuild)
            {
                core.Select = null;
                core.SelectFunctionBody = default;
            }
            else if (mode == InnerProjectionMode.PreserveWithExtras)
            {
                var aliasText = q.From.Alias?.Value ?? "_doc";
                if (TryExtendInnerProjectionBody(q.SelectFunctionBody.FunctionText, extras, aliasText, out var newBody) == false)
                    return null;

                core.SelectFunctionBody = (newBody, null, null);
            }

            var prefix = core.ToString();
            if (string.IsNullOrWhiteSpace(prefix))
                return null;

            prefix = prefix.TrimEnd();
            prefix = prefix.Replace("\r\n", "\n", StringComparison.Ordinal);

            const string nl = "\n";
            var sb = new StringBuilder();
            sb.Append(prefix);

            if (mode == InnerProjectionMode.Rebuild)
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

        private static InnerProjectionMode ClassifyInnerProjection(
            IReadOnlyList<string> projectionCols,
            IReadOnlyCollection<string> innerKeys,
            out List<string> extras)
        {
            extras = null;

            if (projectionCols == null || innerKeys == null || innerKeys.Count == 0)
                return InnerProjectionMode.Rebuild;

            if (ProjectionKeysEqual(projectionCols, innerKeys))
                return InnerProjectionMode.PreserveExact;

            var innerSet = new HashSet<string>(innerKeys, StringComparer.OrdinalIgnoreCase);
            if (innerSet.Count != innerKeys.Count)
                return InnerProjectionMode.Rebuild;

            var seenBusiness = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenExtras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var extrasList = new List<string>();

            foreach (var c in projectionCols)
            {
                if (string.IsNullOrWhiteSpace(c))
                    return InnerProjectionMode.Rebuild;

                if (string.Equals(c, "id()", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c, "json()", StringComparison.OrdinalIgnoreCase))
                {
                    if (seenExtras.Add(c) == false)
                        return InnerProjectionMode.Rebuild;

                    extrasList.Add(c);
                    continue;
                }

                if (innerSet.Contains(c) == false)
                    return InnerProjectionMode.Rebuild;

                if (seenBusiness.Add(c) == false)
                    return InnerProjectionMode.Rebuild;
            }

            if (seenBusiness.Count != innerKeys.Count)
                return InnerProjectionMode.Rebuild;

            if (extrasList.Count == 0)
                return InnerProjectionMode.PreserveExact;

            extras = extrasList;
            return InnerProjectionMode.PreserveWithExtras;
        }

        // Appends id()/json() properties to the inner object-projection body via Acornima's parsed AST.
        private static bool TryExtendInnerProjectionBody(string functionText, List<string> extras, string aliasText, out string newBody)
        {
            newBody = null;

            if (extras is not { Count: > 0 })
                return false;

            if (string.IsNullOrWhiteSpace(aliasText))
                return false;

            if (TryParseProjectionObjectBody(functionText, out var obj) == false)
                return false;

            if (obj.Properties.Count == 0)
                return false;

            int insertAt = obj.Properties[^1].Range.End - ReturnPrefix.Length;
            if ((uint)insertAt > (uint)functionText.Length)
                return false;

            var sb = new StringBuilder(functionText.Length + extras.Count * 32);
            sb.Append(functionText, 0, insertAt);

            foreach (var extra in extras)
            {
                sb.Append(", ");
                if (string.Equals(extra, "id()", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("\"id()\": id(").Append(aliasText).Append(')');
                }
                else if (string.Equals(extra, "json()", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("\"json()\": ").Append(aliasText);
                }
                else
                {
                    return false;
                }
            }

            sb.Append(functionText, insertAt, functionText.Length - insertAt);

            newBody = sb.ToString();
            return true;
        }

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

        // Prefix to coerce an object-projection body into a valid JS script; AST Range offsets are shifted by this length.
        private const string ReturnPrefix = "return ";

        private static bool TryParseProjectionObjectBody(string functionText, out JsAst.ObjectExpression obj)
        {
            obj = null;

            if (string.IsNullOrWhiteSpace(functionText))
                return false;

            JsAst.Script script;
            try
            {
                var parser = new Acornima.Parser(new Acornima.ParserOptions { AllowReturnOutsideFunction = true, Tolerant = false });
                script = parser.ParseScript(ReturnPrefix + functionText);
            }
            catch
            {
                return false;
            }

            if (script?.Body is not { Count: 1 } body)
                return false;

            if (body[0] is not JsAst.ReturnStatement ret || ret.Argument is not JsAst.ObjectExpression o)
                return false;

            foreach (var node in o.Properties)
            {
                if (node is not JsAst.Property { Computed: false })
                    return false;
            }

            obj = o;
            return true;
        }

        private static bool TryGetSelectFunctionBodyObjectKeys(string functionText, out List<string> keys)
        {
            keys = null;

            if (TryParseProjectionObjectBody(functionText, out var obj) == false)
                return false;

            var result = new List<string>(capacity: obj.Properties.Count);
            foreach (var node in obj.Properties)
            {
                var prop = (JsAst.Property)node;

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

        // Raven grouped RQL uses count() with no argument.
        private static bool IsCountFunction(string name) =>
            string.Equals(name, "count", StringComparison.OrdinalIgnoreCase);

        // Plain ASCII only; anything requiring quoting returns null so the caller drops the shape.
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

            // Escape backslashes first so the \ we prepend to " on the next pass isn't double-escaped.
            var escaped = s
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            return "\"" + escaped + "\"";
        }
    }
}
