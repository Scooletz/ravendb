using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using PgSqlParser;
using Raven.Client;
using Raven.Client.Documents.Queries;
using Raven.Client.Documents.Session;
using Raven.Server.Documents;
using Raven.Server.Logging;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Logging;
using Sparrow.Server.Logging;

namespace Raven.Server.Integrations.PostgreSQL.Translation
{
    internal static class PgSqlToRqlTranslator
    {
        private static readonly RavenLogger Logger = RavenLogManager.Instance.GetLoggerForServer(typeof(PgSqlToRqlTranslator));

        private const string UnsupportedSelectAggregateMessage = "Unsupported SELECT aggregate";
        private const string UnsupportedSelectProjectionMessage = "Unsupported SELECT projection";
        private const string MixedAggregateAndNonAggregateMessage = "Mixing aggregates and non-aggregated columns is not supported yet.";
        private const string UnsupportedDistinctMessage = "Unsupported DISTINCT";
        private const string UnsupportedGroupByMessage = "Unsupported GROUP BY";
        private const string UnsupportedOrderByForGroupByMessage = "Unsupported ORDER BY for GROUP BY";
        private const string UnsupportedJoinMessage = "Unsupported JOIN shape";

        public static bool TryParse(string sql, int[] parameterTypes, out string rql)
            => TryParse(sql, parameterTypes, documentDatabase: null, out rql, out _);

        public static bool TryParse(string sql, int[] parameterTypes, DocumentDatabase documentDatabase, out string rql)
            => TryParse(sql, parameterTypes, documentDatabase, out rql, out _);

        // The documentDatabase overload lets the ORDER BY translator infer numeric vs string
        // ordering by sampling the collection's first document. Without it, every ORDER BY falls
        // back to PG's default (alphabetic), which produces "10 < 9" results for numeric fields.
        //
        // hasExplicitProjection tells the caller whether the SQL had a real projection (e.g.
        // `SELECT a, b FROM t`) or a wildcard (`SELECT * FROM t`). The caller uses it to decide
        // whether to suppress RqlQuery's auto-included id()/json() columns — if the user
        // explicitly listed columns, they shouldn't get magic extras tacked on.
        public static bool TryParse(string sql, int[] parameterTypes, DocumentDatabase documentDatabase, out string rql, out bool hasExplicitProjection)
        {
            rql = null;
            hasExplicitProjection = false;

            if (Logger.IsDebugEnabled)
                Logger.Debug($"{nameof(PgSqlToRqlTranslator)}.{nameof(TryParse)} invoked with SQL: {sql}");

            try
            {
                var parseResult = Parser.Parse(sql);

                if (parseResult.IsSuccess == false || parseResult.Value == null)
                {
                    if (Logger.IsDebugEnabled)
                    {
                        var parseError = parseResult.Error;
                        if (parseError != null)
                            Logger.Debug($"{nameof(PgSqlToRqlTranslator)} parse error. Message: {parseError.Message}. SQL: {sql}");
                        else
                            Logger.Debug($"{nameof(PgSqlToRqlTranslator)} parse error. SQL: {sql}");
                    }

                    return LogFailure("parse error");
                }

                if (parseResult.Value.Stmts == null || parseResult.Value.Stmts.Count == 0)
                    throw new NotSupportedException("No statements found in query.");

                var stmt = parseResult.Value.Stmts[0];
                if (stmt?.Stmt?.SelectStmt == null)
                    throw new NotSupportedException("Only SELECT statements are supported.");

                hasExplicitProjection = HasExplicitProjection(stmt.Stmt.SelectStmt);
                rql = TranslateSelectStatement(stmt.Stmt.SelectStmt, documentDatabase);
                return LogSuccess(sql, rql);
            }
            catch (NotSupportedException ex)
            {
                rql = null;
                var reason = string.IsNullOrWhiteSpace(ex.Message)
                    ? "unsupported query shape"
                    : $"unsupported query shape: {ex.Message}";
                return LogFailure(reason);
            }
        }

        private static bool LogSuccess(string sql, string rql)
        {
            if (Logger.IsInfoEnabled)
                Logger.Info($"{nameof(PgSqlToRqlTranslator)} translated SQL to RQL. SQL: {sql}. RQL: {rql}");
            return true;
        }

        private static bool LogFailure(string reason)
        {
            if (Logger.IsDebugEnabled)
                Logger.Debug($"{nameof(PgSqlToRqlTranslator)} did not translate SQL. Reason: {reason}");
            return false;
        }

        private static string TranslateSelectStatement(SelectStmt selectStmt, DocumentDatabase documentDatabase = null)
        {
            if (selectStmt.FromClause is [{ JoinExpr: not null }])
                return TranslateSimpleJoin(selectStmt);

            if (selectStmt.FromClause is not [{ RangeVar: { Relname: var relname } rangeVar }])
                throw new NotSupportedException("FROM clause with collection or index name is required");

            var isIndex = string.Equals(rangeVar.Schemaname, "indexes", StringComparison.OrdinalIgnoreCase);
            var isGroupBy = selectStmt.GroupClause != null && selectStmt.GroupClause.Count > 0;
            var q = new AsyncDocumentQuery<JObject>(session: null, indexName: isIndex ? relname : null, collectionName: isIndex ? null : relname, isGroupBy: isGroupBy);
            var fromAlias = rangeVar.Alias?.Aliasname;

            // Build WHERE clause (must be applied before GROUP BY)
            if (selectStmt.WhereClause != null)
                TranslateWhereClause(q, selectStmt.WhereClause, fromAlias);

            if (isGroupBy)
            {
                ApplyGroupBy(q, selectStmt, fromAlias);
            }
            else
            {
                ApplySelectProjection(q, selectStmt, fromAlias);

                // Build ORDER BY clause. Type-infer sort fields from a doc sample (when the
                // database is available) so numeric fields sort numerically instead of falling
                // back to alphabetic. Indexes (Schemaname = "indexes") are skipped — they don't
                // have a "first document" in the collection-sampling sense.
                if (selectStmt.SortClause != null && selectStmt.SortClause.Count > 0)
                {
                    var sortTypeMap = isIndex ? null : TryBuildSortTypeMap(documentDatabase, relname, selectStmt.SortClause, fromAlias);
                    TranslateOrderBy(q, selectStmt.SortClause, fromAlias, sortTypeMap);
                }
            }

            // Build OFFSET clause. Fail the whole translation rather than silently
            // dropping an OFFSET we can't represent (parameter placeholders, non-integer
            // constants, …) — silently ignoring the bound would return extra rows.
            if (selectStmt.LimitOffset != null)
            {
                var offset = TranslateLimit(selectStmt.LimitOffset);
                if (offset == null)
                    throw new NotSupportedException("Unsupported OFFSET expression (only integer literals are supported)");
                q.Skip(offset.Value);
            }

            // Build LIMIT clause. Same fail-hard policy as OFFSET.
            if (selectStmt.LimitCount != null)
            {
                var limit = TranslateLimit(selectStmt.LimitCount);
                if (limit == null)
                    throw new NotSupportedException("Unsupported LIMIT expression (only integer literals are supported)");
                q.Take(limit.Value);
            }

            // Prefer the official query text emitted by IndexQuery when possible.
            // Falls back to ToString() which also returns pure RQL.
            var indexQuery = q.GetIndexQuery();

            var queryText = indexQuery?.Query ?? q.ToString();
            var parameters = indexQuery?.QueryParameters;

            if (parameters != null)
                queryText = InlineQueryParameters(queryText, parameters);

            return queryText;
        }

        private static string InlineQueryParameters(string rql, Parameters parameters)
        {
            if (string.IsNullOrWhiteSpace(rql))
                return rql;

            if (parameters == null || parameters.Count == 0)
                return rql;

            var sb = new StringBuilder(rql.Length);
            for (int i = 0; i < rql.Length; i++)
            {
                var ch = rql[i];
                if (ch != '$' || i + 2 >= rql.Length || rql[i + 1] != 'p')
                {
                    sb.Append(ch);
                    continue;
                }

                int start = i;
                i += 2;

                int numStart = i;
                while (i < rql.Length && char.IsDigit(rql[i]))
                    i++;

                if (numStart == i)
                {
                    sb.Append(rql.AsSpan(start, (i - start) + 1));
                    continue;
                }

                var paramName = rql.Substring(start + 1, i - (start + 1)); // p0
                if (parameters.TryGetValue(paramName, out var value))
                {
                    if (value is IEnumerable enumerable and not string)
                        sb.Append(FormatRqlListItems(enumerable));
                    else
                        sb.Append(FormatRqlLiteral(value));
                }
                else
                {
                    sb.Append('$');
                    sb.Append(paramName);
                }
                i--; // compensate for for-loop increment
            }

            return sb.ToString();
        }

        // Carries a $N parameter index through AsyncDocumentQuery's auto-parameterization
        // (TransformValue leaves unknown reference types untouched) so InlineQueryParameters
        // can rewrite the generated $pN token into an RQL parameter reference ($N) that maps to
        // the Bind-time Parameters dict — instead of inlining a literal we don't have yet.
        private sealed record PgBoundParameterReference(int OneBasedIndex);

        private static string FormatRqlLiteral(object value)
        {
            if (value == null)
                return "null";

            // A $N placeholder: emit an RQL parameter reference rather than a literal. The PG
            // parameter index doubles as the RQL parameter name (RQL allows numeric names), and
            // PgQuery.Bind keys the Parameters dict by the same 1-based index.
            if (value is PgBoundParameterReference paramRef)
                return "$" + paramRef.OneBasedIndex.ToString(CultureInfo.InvariantCulture);

            if (value is IEnumerable enumerable and not string)
                return FormatRqlList(enumerable);

            if (value is string s)
                return QuoteString(s);

            if (value is bool b)
                return b ? "true" : "false";

            if (value is int or long or short or byte or uint or ulong or ushort or sbyte)
                return Convert.ToString(value, CultureInfo.InvariantCulture);

            if (value is float or double or decimal)
                return Convert.ToString(value, CultureInfo.InvariantCulture);

            if (value is DateTime dt)
                return QuoteString(dt.ToString("O", CultureInfo.InvariantCulture));

            if (value is DateTimeOffset dto)
                return QuoteString(dto.ToString("O", CultureInfo.InvariantCulture));

            throw new NotSupportedException($"Unsupported query parameter type '{value.GetType()}'.");
        }

        private static string FormatRqlList(IEnumerable values)
        {
            var sb = new StringBuilder();
            sb.Append('(');

            sb.Append(FormatRqlListItems(values));

            sb.Append(')');
            return sb.ToString();
        }

        private static string FormatRqlListItems(IEnumerable values)
        {
            var sb = new StringBuilder();

            bool first = true;
            foreach (var item in values)
            {
                if (first == false)
                    sb.Append(", ");

                sb.Append(FormatRqlLiteral(item));
                first = false;
            }

            return sb.ToString();
        }

        private static string QuoteString(string s)
        {
            if (s == null)
                return "null";

            return "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        private static string TranslateSimpleJoin(SelectStmt selectStmt)
        {
            if (selectStmt.FromClause is not [{ JoinExpr: { } joinExpr }])
                throw new NotSupportedException("FROM clause with join is required");

            if (IsSelectStar(selectStmt.TargetList ?? []) == false)
                throw new NotSupportedException(UnsupportedJoinMessage);

            if (selectStmt.WhereClause != null || selectStmt.GroupClause is { Count: > 0 } || selectStmt.SortClause is { Count: > 0 } ||
                selectStmt.LimitCount != null || selectStmt.LimitOffset != null || selectStmt.DistinctClause is { Count: > 0 })
                throw new NotSupportedException(UnsupportedJoinMessage);

            if (TryExtractSimpleJoinInfo(joinExpr, out var drivingCollection, out var drivingAlias, out var loadPath, out var loadAlias) == false)
                throw new NotSupportedException(UnsupportedJoinMessage);

            return $"from '{drivingCollection}' as {drivingAlias} load {drivingAlias}.{loadPath} as {loadAlias} select {{ {drivingAlias}: {drivingAlias}, {loadAlias}: {loadAlias} }}";
        }

        private static bool TryExtractSimpleJoinInfo(
            JoinExpr joinExpr,
            out string drivingCollection,
            out string drivingAlias,
            out string loadPath,
            out string loadAlias)
        {
            drivingCollection = string.Empty;
            drivingAlias = string.Empty;
            loadPath = string.Empty;
            loadAlias = string.Empty;

            if (TryGetRangeVarWithAlias(joinExpr.Larg, out var leftCollection, out var leftAlias) == false ||
                TryGetRangeVarWithAlias(joinExpr.Rarg, out var rightCollection, out var rightAlias) == false)
                return false;

            if (joinExpr.Quals?.AExpr is not { Kind: A_Expr_Kind.AexprOp, Name.Count: > 0 } onExpr)
                return false;

            var operatorName = GetStringValue(onExpr.Name[0]) ?? string.Empty;
            if (string.Equals(operatorName, "=", StringComparison.Ordinal) == false)
                return false;

            if (onExpr.Lexpr?.ColumnRef == null || onExpr.Rexpr?.ColumnRef == null)
                return false;

            var leftRef = ExtractFieldName(onExpr.Lexpr);
            var rightRef = ExtractFieldName(onExpr.Rexpr);
            if (string.IsNullOrWhiteSpace(leftRef) || string.IsNullOrWhiteSpace(rightRef))
                return false;

            if (TryParseQualifiedField(leftRef, out var leftRefAlias, out var leftRefField) == false ||
                TryParseQualifiedField(rightRef, out var rightRefAlias, out var rightRefField) == false)
                return false;

            if (string.Equals(leftRefField, "id", StringComparison.OrdinalIgnoreCase))
            {
                drivingAlias = rightRefAlias;
                loadPath = rightRefField;
                loadAlias = leftRefAlias;
            }
            else if (string.Equals(rightRefField, "id", StringComparison.OrdinalIgnoreCase))
            {
                drivingAlias = leftRefAlias;
                loadPath = leftRefField;
                loadAlias = rightRefAlias;
            }
            else
            {
                return false;
            }

            if (string.Equals(drivingAlias, leftAlias, StringComparison.OrdinalIgnoreCase))
                drivingCollection = leftCollection;
            else if (string.Equals(drivingAlias, rightAlias, StringComparison.OrdinalIgnoreCase))
                drivingCollection = rightCollection;
            else
                return false;

            return true;
        }

        private static bool TryGetRangeVarWithAlias(Node node, out string collection, out string alias)
        {
            collection = string.Empty;
            alias = string.Empty;

            var rv = node.RangeVar;
            if (rv?.Relname == null)
                return false;

            if (string.IsNullOrWhiteSpace(rv.Schemaname) == false)
                return false;

            collection = rv.Relname;
            alias = rv.Alias?.Aliasname ?? rv.Relname;
            return string.IsNullOrWhiteSpace(alias) == false;
        }

        private static bool TryParseQualifiedField(string field, out string alias, out string path)
        {
            alias = string.Empty;
            path = string.Empty;

            var idx = field.IndexOf('.', StringComparison.Ordinal);
            if (idx <= 0 || idx >= field.Length - 1)
                return false;

            alias = field[..idx];
            path = field[(idx + 1)..];
            return string.IsNullOrWhiteSpace(alias) == false && string.IsNullOrWhiteSpace(path) == false;
        }

        private static void ApplyGroupBy(AsyncDocumentQuery<JObject> q, SelectStmt selectStmt, string fromAlias)
        {
            if (selectStmt.GroupClause is not { Count: > 0 })
                throw new NotSupportedException(UnsupportedGroupByMessage);

            var groupFieldNames = new List<string>(capacity: selectStmt.GroupClause.Count);
            foreach (var groupKeyNode in selectStmt.GroupClause)
            {
                if (groupKeyNode.ColumnRef == null)
                    throw new NotSupportedException(UnsupportedGroupByMessage);

                var name = ExtractFieldName(groupKeyNode, fromAlias);
                if (string.IsNullOrWhiteSpace(name))
                    throw new NotSupportedException(UnsupportedGroupByMessage);

                groupFieldNames.Add(name);
            }

            var targets = selectStmt.TargetList;
            if (targets == null || targets.Count == 0)
                throw new NotSupportedException(UnsupportedGroupByMessage);

            if (IsSelectStar(targets))
                throw new NotSupportedException(UnsupportedGroupByMessage);

            if (selectStmt.DistinctClause is { Count: > 0 })
                throw new NotSupportedException(UnsupportedGroupByMessage);

            bool hasAnyAggregate = false;
            foreach (var t in targets)
            {
                if (t.ResTarget?.Val?.FuncCall != null)
                {
                    hasAnyAggregate = true;
                    break;
                }
            }

            // Distinct-rows shape (PowerBI's "fill a slicer/dropdown" probe):
            //     SELECT col1, col2 ... FROM t GROUP BY col1, col2 [LIMIT N]
            // No aggregates — the GROUP BY is being used as a DISTINCT mechanism.
            //
            // Single-column: emit RQL `select distinct <field>` — Raven dedupes the projection.
            // Multi-column: use `group by <fields> select <fields>` instead. RQL's `select
            // distinct <a>, <b>` is NOT a tuple-distinct — it dedupes by first-field semantics
            // and leaves duplicate tuples in the result, which then breaks PowerBI's mashup
            // engine ("more than one row in the index table matching to the current row"). The
            // group-by form is the canonical tuple-dedup in RQL: every (col1, col2) pair becomes
            // a group, and projecting the group keys gives back exactly the distinct tuples.
            if (hasAnyAggregate == false)
            {
                var groupSet = new HashSet<string>(groupFieldNames, StringComparer.OrdinalIgnoreCase);
                var projected = new List<string>(targets.Count);
                foreach (var t in targets)
                {
                    var val = t.ResTarget?.Val;
                    if (val?.ColumnRef == null)
                        throw new NotSupportedException(UnsupportedGroupByMessage);

                    var field = ExtractFieldName(val, fromAlias);
                    if (string.IsNullOrWhiteSpace(field) || groupSet.Contains(field) == false)
                        throw new NotSupportedException(UnsupportedGroupByMessage);

                    projected.Add(field);
                }

                if (groupFieldNames.Count == 1)
                {
                    q.SelectFields<JObject>(projected.ToArray());
                    q.Distinct();
                }
                else
                {
                    // Multi-key tuple-distinct via group-by. SelectFields after a multi-key
                    // GroupBy projects each group's key values back as columns.
                    var firstKey = groupFieldNames[0];
                    var restKeys = groupFieldNames.Count > 1
                        ? groupFieldNames.GetRange(1, groupFieldNames.Count - 1).ToArray()
                        : Array.Empty<string>();
                    q.GroupBy(firstKey, restKeys);
                    q.SelectFields<JObject>(projected.ToArray());
                }
                return;
            }

            // Aggregate path: only single-column GROUP BY is representable in RQL today.
            if (groupFieldNames.Count != 1)
                throw new NotSupportedException(UnsupportedGroupByMessage);

            var groupFieldName = groupFieldNames[0];

            var projections = new List<string>(capacity: targets.Count);
            foreach (var t in targets)
            {
                var val = t.ResTarget?.Val;
                if (val == null)
                    throw new NotSupportedException(UnsupportedGroupByMessage);

                projections.Add(BuildProjectionForGroupByTarget(val, groupFieldName));
            }

            q.GroupBy(groupFieldName);
            q.SelectFields<JObject>(projections.ToArray());

            if (selectStmt.SortClause != null && selectStmt.SortClause.Count > 0)
                TranslateOrderByForGroupBy(q, selectStmt.SortClause, groupFieldName, projections);
        }

        private static void TranslateOrderByForGroupBy(
            AsyncDocumentQuery<JObject> q,
            Google.Protobuf.Collections.RepeatedField<Node> sortClause,
            string groupFieldName,
            IReadOnlyList<string> projections)
        {
            foreach (var sortNode in sortClause)
            {
                var sortBy = sortNode.SortBy;
                if (sortBy == null)
                    continue;

                string orderExpr = null;

                if (sortBy.Node?.ColumnRef != null)
                {
                    var fieldName = ExtractFieldName(sortBy.Node);
                    if (string.IsNullOrWhiteSpace(fieldName))
                        throw new NotSupportedException(UnsupportedOrderByForGroupByMessage);

                    if (string.Equals(fieldName, groupFieldName, StringComparison.OrdinalIgnoreCase) == false)
                        throw new NotSupportedException(UnsupportedOrderByForGroupByMessage);

                    orderExpr = projections.First(p => string.Equals(p, groupFieldName, StringComparison.OrdinalIgnoreCase));
                }
                else if (sortBy.Node?.FuncCall != null)
                {
                    var projection = BuildAggregateProjectionForGroupByOrderBy(sortBy.Node.FuncCall);

                    if (projections.Any(p => string.Equals(p, projection, StringComparison.OrdinalIgnoreCase)) == false)
                        throw new NotSupportedException(UnsupportedOrderByForGroupByMessage);

                    orderExpr = $"'{projections.First(p => string.Equals(p, projection, StringComparison.OrdinalIgnoreCase))}'";
                }
                else
                {
                    throw new NotSupportedException(UnsupportedOrderByForGroupByMessage);
                }

                if (sortBy.SortbyDir == SortByDir.SortbyDesc)
                    q.OrderByDescending(orderExpr);
                else
                    q.OrderBy(orderExpr);
            }
        }

        private static void ApplySelectProjection(AsyncDocumentQuery<JObject> q, SelectStmt selectStmt, string fromAlias)
        {
            var targets = selectStmt.TargetList;
            if (targets == null || targets.Count == 0)
                return;

            if (IsSelectStar(targets))
                return;

            var isDistinct = selectStmt.DistinctClause is { Count: > 0 };
            var anyAgg = targets.Any(IsAggregateTarget);
            var allAgg = targets.All(IsAggregateTarget);

            if (isDistinct && anyAgg)
                throw new NotSupportedException(UnsupportedDistinctMessage);

            if (anyAgg && !allAgg)
                throw new NotSupportedException(MixedAggregateAndNonAggregateMessage);

            if (allAgg)
            {
                q.SelectFields<JObject>(BuildAggregateProjections(targets));
                return;
            }

            var (projectionFields, projectionAliases) = BuildColumnProjections(targets, fromAlias);
            if (isDistinct && projectionFields.Length != 1)
                throw new NotSupportedException(UnsupportedDistinctMessage);

            if (projectionFields.Length == 0)
                return;

            // QueryData with distinct Fields vs Projections lets us emit `<expr> as <alias>` in
            // RQL when they differ — required to preserve PowerBI's SQL aliases on constant
            // projections like `1 as "c0"`. When the SQL alias matches the field expression
            // (the common case for column references), FieldsToFetchToken skips the `as` clause.
            q.SelectFields<JObject>(new QueryData(projectionFields, projectionAliases) { IsProjectInto = true });
            if (isDistinct)
                q.Distinct();
        }

        private static bool IsSelectStar(IReadOnlyList<Node> targetList)
        {
            return
                targetList.Count == 1 &&
                targetList[0].ResTarget?.Val?.ColumnRef?.Fields is { Count: 1 } fields &&
                fields[0].AStar != null;
        }

        // Distinguishes `SELECT a, b FROM t` (explicit, count-it) from `SELECT * FROM t` (wildcard,
        // don't count it). The empty-list case is treated as wildcard so we don't accidentally
        // suppress auto-include for queries the translator hasn't fully recognized yet.
        private static bool HasExplicitProjection(SelectStmt selectStmt)
        {
            var targets = selectStmt?.TargetList;
            if (targets == null || targets.Count == 0)
                return false;
            return IsSelectStar(targets) == false;
        }

        private static bool IsAggregateTarget(Node target)
        {
            return target.ResTarget?.Val?.FuncCall != null;
        }

        // Returns parallel arrays: projectionFields are the RQL expressions, projectionAliases
        // are the SQL `AS <alias>` names (defaulting to the field expression itself when the
        // SQL projection had no explicit alias). QueryData.Fields/Projections renders
        // `<field> as <alias>` when the two differ — which is the only way to preserve
        // PowerBI's aliases for constant projections like `1 as "c0"` (their row-preview
        // queries emit those and would otherwise produce a column-count mismatch).
        private static (string[] Fields, string[] Aliases) BuildColumnProjections(IReadOnlyList<Node> targetList, string fromAlias)
        {
            var projectionFields = new List<string>(capacity: targetList.Count);
            var projectionAliases = new List<string>(capacity: targetList.Count);

            foreach (var t in targetList)
            {
                var resTarget = t.ResTarget;
                var val = resTarget?.Val;
                if (val == null)
                    throw new NotSupportedException(UnsupportedSelectProjectionMessage);

                var fieldName = TranslateSelectTargetValue(val, fromAlias);
                if (fieldName == null)
                    continue; // e.g. PowerBI pseudo-column json()

                projectionFields.Add(fieldName);

                // Use the SQL `AS <alias>` when explicit; otherwise the field expression itself
                // doubles as the alias (matches existing single-arg SelectFields semantics where
                // Fields[i] == Projections[i] and FieldsToFetchToken skips the `as` clause).
                var alias = resTarget.Name;
                projectionAliases.Add(string.IsNullOrWhiteSpace(alias) ? fieldName : alias);
            }

            return (projectionFields.ToArray(), projectionAliases.ToArray());
        }

        private static string TranslateSelectTargetValue(Node val, string fromAlias)
        {
            if (val.ColumnRef != null)
            {
                var fieldName = ExtractFieldName(val, fromAlias);
                if (string.IsNullOrWhiteSpace(fieldName))
                    throw new NotSupportedException("Unsupported column reference in SELECT projection");

                // Synthetic columns: `json` (or legacy `json()`) is metadata-blob, emitted by
                // the RQL row writer separately so we drop it from the projection. `id` (or
                // legacy `id()`) maps to RQL's id() function call regardless of which surface
                // name the client used.
                if (PgSyntheticColumns.IsJsonColumn(fieldName))
                    return null;

                if (PgSyntheticColumns.IsDocumentIdColumn(fieldName))
                    return "id()";

                return fieldName;
            }

            // PowerBI's row-preview queries decorate their projection list with constant
            // markers (e.g. `1 as "c0"`) so the client can count back a known fixed shape.
            // Without literal handling here the translator would throw NotSupported and the
            // whole query would fall through to a code path that strips the constant
            // (losing the column entirely), and PowerBI then reports
            // `Field count mismatch when mapping column types. 12 vs 11`.
            if (val.AConst != null)
            {
                if (TryRenderRqlLiteral(val.AConst, out var literal))
                    return literal;
            }

            throw new NotSupportedException(UnsupportedSelectProjectionMessage);
        }

        // Renders the three concrete AConst kinds — Ival (integer), Fval (float), Sval
        // (string) — plus the all-null case (SQL NULL literal). Returns false for shapes the
        // RQL select clause can't accept verbatim, letting the caller fall through to the
        // unsupported-projection error. Strings are rendered with single-quote RQL syntax
        // and inner single quotes doubled (RQL's escape convention), matching how the WHERE
        // translator emits its literals.
        private static bool TryRenderRqlLiteral(A_Const c, out string rendered)
        {
            rendered = null;
            if (c == null)
                return false;

            if (c.Ival != null)
            {
                rendered = c.Ival.Ival.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (c.Fval != null && string.IsNullOrEmpty(c.Fval.Fval) == false)
            {
                rendered = c.Fval.Fval;
                return true;
            }

            if (c.Sval != null && c.Sval.Sval != null)
            {
                rendered = "'" + c.Sval.Sval.Replace("'", "''") + "'";
                return true;
            }

            if (c.Boolval != null)
            {
                rendered = c.Boolval.Boolval ? "true" : "false";
                return true;
            }

            // All null components → SQL NULL.
            if (c.Ival == null && c.Fval == null && c.Sval == null && c.Boolval == null)
            {
                rendered = "null";
                return true;
            }

            return false;
        }

        private static string[] BuildAggregateProjections(IReadOnlyList<Node> targetList)
        {
            var projections = new List<string>(capacity: targetList.Count);

            foreach (var t in targetList)
            {
                var funcCall = t.ResTarget?.Val?.FuncCall;
                if (funcCall == null)
                    throw new NotSupportedException(UnsupportedSelectAggregateMessage);

                var funcName = GetFuncNameOrThrow(funcCall);

                switch (funcName)
                {
                    case "count":
                        projections.Add(BuildCountProjection(funcCall));
                        break;

                    case "sum":
                    case "avg":
                        projections.Add(BuildSingleColumnAggregateProjection(funcName, funcCall));
                        break;

                    default:
                        throw new NotSupportedException(UnsupportedSelectAggregateMessage);
                }
            }

            return projections.ToArray();
        }

        private static string GetFuncNameOrThrow(FuncCall funcCall)
        {
            if (funcCall.Funcname == null || funcCall.Funcname.Count == 0 || funcCall.Funcname[0].String == null)
                throw new NotSupportedException(UnsupportedSelectAggregateMessage);

            return (funcCall.Funcname[0].String.Sval ?? string.Empty).ToLowerInvariant();
        }

        private static Node GetSingleArgOrThrow(FuncCall funcCall)
        {
            if (funcCall.Args == null || funcCall.Args.Count != 1)
                throw new NotSupportedException(UnsupportedSelectAggregateMessage);

            return funcCall.Args[0];
        }

        private static string BuildCountProjection(FuncCall funcCall)
        {
            if (funcCall.AggStar)
                return "count()";

            var countArg = GetSingleArgOrThrow(funcCall);
            if (countArg.ColumnRef != null)
            {
                var countFieldName = ExtractFieldName(countArg);
                if (string.IsNullOrWhiteSpace(countFieldName))
                    throw new NotSupportedException(UnsupportedSelectAggregateMessage);
                return $"count({countFieldName})";
            }

            if (countArg.AConst != null)
                return "count()";

            throw new NotSupportedException(UnsupportedSelectAggregateMessage);
        }

        private static string BuildSingleColumnAggregateProjection(string funcName, FuncCall funcCall)
        {
            var arg0 = GetSingleArgOrThrow(funcCall);
            if (arg0?.ColumnRef == null)
                throw new NotSupportedException(UnsupportedSelectAggregateMessage);

            var fieldName = ExtractFieldName(arg0);
            if (string.IsNullOrWhiteSpace(fieldName))
                throw new NotSupportedException(UnsupportedSelectAggregateMessage);

            return $"{funcName}({fieldName})";
        }

        private static string BuildProjectionForGroupByTarget(Node val, string groupFieldName)
        {
            if (val.ColumnRef != null)
            {
                var field = ExtractFieldName(val);
                if (string.Equals(field, groupFieldName, StringComparison.OrdinalIgnoreCase) == false)
                    throw new NotSupportedException(UnsupportedGroupByMessage);

                return groupFieldName;
            }

            if (val.FuncCall != null)
            {
                var funcName = GetFuncNameOrThrow(val.FuncCall);
                return funcName switch
                {
                    "count" => BuildCountProjection(val.FuncCall),
                    "sum" or "avg" => BuildSingleColumnAggregateProjection(funcName, val.FuncCall),
                    _ => throw new NotSupportedException(UnsupportedGroupByMessage)
                };
            }

            throw new NotSupportedException(UnsupportedGroupByMessage);
        }

        private static string BuildAggregateProjectionForGroupByOrderBy(FuncCall funcCall)
        {
            var funcName = GetFuncNameOrThrow(funcCall);
            return funcName switch
            {
                "count" => BuildCountProjection(funcCall),
                "sum" or "avg" => BuildSingleColumnAggregateProjection(funcName, funcCall),
                _ => throw new NotSupportedException(UnsupportedOrderByForGroupByMessage)
            };
        }

        private static void TranslateWhereClause(AsyncDocumentQuery<JObject> q, Node whereNode, string fromAlias)
        {
            if (SqlWhereParser.TryParse(whereNode, fromAlias, out var parsed) == false)
                throw new NotSupportedException("Unsupported WHERE clause");

            EmitWhere(q, parsed, wrapInSubclause: false);
        }

        private static void EmitWhere(AsyncDocumentQuery<JObject> q, ParsedWhere parsed, bool wrapInSubclause)
        {
            if (wrapInSubclause)
                q.OpenSubclause();

            switch (parsed)
            {
                case ParsedAnd a:
                    for (int i = 0; i < a.Children.Count; i++)
                    {
                        if (i > 0)
                            q.AndAlso();
                        // A nested OR under an AND needs parenthesisation to keep precedence.
                        EmitWhere(q, a.Children[i], wrapInSubclause: a.Children[i] is ParsedOr);
                    }
                    break;

                case ParsedOr o:
                    for (int i = 0; i < o.Children.Count; i++)
                    {
                        if (i > 0)
                            q.OrElse();
                        // AND has higher precedence than OR in RQL, so OR's children don't need wrapping.
                        EmitWhere(q, o.Children[i], wrapInSubclause: false);
                    }
                    break;

                case ParsedNot:
                    // General SQL→RQL flow does not currently support NOT. PowerBI translator handles it separately.
                    throw new NotSupportedException("NOT is not supported in general SQL→RQL WHERE translation");

                case ParsedBinary b:
                {
                    var field = JoinPath(b.FieldPath);
                    var value = ToQueryValue(b.Value);
                    switch (b.Operator)
                    {
                        case "=":   q.WhereEquals(field, value); break;
                        case "!=":
                        case "<>":  q.WhereNotEquals(field, value); break;
                        case "<":   q.WhereLessThan(field, value); break;
                        case "<=":  q.WhereLessThanOrEqual(field, value); break;
                        case ">":   q.WhereGreaterThan(field, value); break;
                        case ">=":  q.WhereGreaterThanOrEqual(field, value); break;
                        default:    throw new NotSupportedException($"Unsupported operator '{b.Operator}'");
                    }
                    break;
                }

                case ParsedIn i:
                {
                    var field = JoinPath(i.FieldPath);
                    var values = new List<object>(i.Values.Count);
                    foreach (var v in i.Values)
                    {
                        if (v.Kind != ParsedValueKind.Parameter && v.Raw == null)
                            throw new NotSupportedException("Unsupported IN (null value)");
                        values.Add(ToQueryValue(v));
                    }
                    if (i.Negated)
                        throw new NotSupportedException("NOT IN is not supported in general SQL→RQL WHERE translation");
                    q.WhereIn(field, values);
                    break;
                }

                case ParsedBetween bt:
                {
                    var field = JoinPath(bt.FieldPath);
                    if ((bt.Lower.Kind != ParsedValueKind.Parameter && bt.Lower.Raw == null) ||
                        (bt.Upper.Kind != ParsedValueKind.Parameter && bt.Upper.Raw == null))
                        throw new NotSupportedException("Unsupported BETWEEN (missing bound)");
                    q.WhereBetween(field, ToQueryValue(bt.Lower), ToQueryValue(bt.Upper));
                    break;
                }

                case ParsedIsNull n:
                {
                    var field = JoinPath(n.FieldPath);
                    if (n.Negated)
                        q.WhereNotEquals(field, null);
                    else
                        q.WhereEquals(field, null);
                    break;
                }

                default:
                    throw new NotSupportedException($"Unsupported WHERE IR node: {parsed?.GetType().Name}");
            }

            if (wrapInSubclause)
                q.CloseSubclause();
        }

        private static string JoinPath(IReadOnlyList<string> fieldPath) => string.Join('.', fieldPath);

        // Maps a parsed WHERE value to the object handed to AsyncDocumentQuery. Literals pass
        // through as-is; a $N placeholder becomes a marker that survives auto-parameterization and
        // is later rewritten to an RQL parameter reference by InlineQueryParameters/FormatRqlLiteral.
        private static object ToQueryValue(ParsedValue value)
            => value.Kind == ParsedValueKind.Parameter
                ? new PgBoundParameterReference((int)value.Raw)
                : value.Raw;

        private static void TranslateOrderBy(AsyncDocumentQuery<JObject> q, Google.Protobuf.Collections.RepeatedField<Node> sortClause, string fromAlias, IReadOnlyDictionary<string, OrderingType> sortTypeMap = null)
        {
            foreach (var sortNode in sortClause)
            {
                if (sortNode.SortBy == null)
                    continue;

                var sortBy = sortNode.SortBy;
                var fieldName = ExtractFieldName(sortBy.Node, fromAlias);

                if (string.IsNullOrEmpty(fieldName))
                    continue;

                // Pick the right ordering: type-inferred from the sampled doc when we have it,
                // otherwise PG-style default (String / alphabetic).
                var ordering = OrderingType.String;
                if (sortTypeMap != null && sortTypeMap.TryGetValue(fieldName, out var inferred))
                    ordering = inferred;

                if (sortBy.SortbyDir == SortByDir.SortbyDesc)
                    q.OrderByDescending(fieldName, ordering);
                else
                    q.OrderBy(fieldName, ordering);
            }
        }

        // Samples the first document of the collection to learn the blittable token type of each
        // ORDER BY field, mapped to RQL OrderingType. Returns null if the database isn't available
        // or the collection is empty — callers fall back to OrderingType.String in that case.
        //
        // Field lookups are case-sensitive against the document's stored property names. This
        // means an unquoted `ORDER BY Freight` (case-folded to `freight` by libpg_query) won't
        // find `Freight` and will fall back to String ordering — the same surface area issue users
        // hit with unquoted WHERE clauses. Quoting (`ORDER BY "Freight"`) preserves case and the
        // type inference kicks in.
        private static Dictionary<string, OrderingType> TryBuildSortTypeMap(
            DocumentDatabase documentDatabase,
            string collection,
            Google.Protobuf.Collections.RepeatedField<Node> sortClause,
            string fromAlias)
        {
            if (documentDatabase == null || string.IsNullOrWhiteSpace(collection))
                return null;

            // Collect candidate field names from the SortClause so we only do property lookups
            // for what we need.
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sortNode in sortClause)
            {
                if (sortNode.SortBy?.Node == null)
                    continue;
                var name = ExtractFieldName(sortNode.SortBy.Node, fromAlias);
                if (string.IsNullOrEmpty(name))
                    continue;
                fieldNames.Add(name);
            }

            if (fieldNames.Count == 0)
                return null;

            BlittableJsonReaderObject sample = null;
            try
            {
                using (documentDatabase.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
                using (context.OpenReadTransaction())
                {
                    foreach (var doc in documentDatabase.DocumentsStorage.GetDocumentsFrom(context, collection, etag: 0, start: 0, take: 1))
                    {
                        sample = doc.Data;
                        break;
                    }

                    if (sample == null)
                        return null;

                    var result = new Dictionary<string, OrderingType>(StringComparer.Ordinal);
                    var prop = default(BlittableJsonReaderObject.PropertyDetails);
                    foreach (var name in fieldNames)
                    {
                        var propIdx = sample.GetPropertyIndex(name);
                        if (propIdx == -1)
                            continue;
                        sample.GetPropertyByIndex(propIdx, ref prop);
                        result[name] = MapTokenToOrderingType(prop.Token);
                    }
                    return result;
                }
            }
            catch
            {
                // Best-effort. Any failure (storage error, collection vanished, etc.) just falls
                // back to default String ordering — never block translation on the type probe.
                return null;
            }
        }

        private static OrderingType MapTokenToOrderingType(BlittableJsonToken token)
            => (token & BlittableJsonReaderBase.TypesMask) switch
            {
                BlittableJsonToken.Integer    => OrderingType.Long,
                BlittableJsonToken.LazyNumber => OrderingType.Double,
                _                              => OrderingType.String,
            };

        private static int? TranslateLimit(Node limitNode)
        {
            return PgSqlAstHelpers.TryReadNonNegativeIntConst(limitNode, out var value) ? value : null;
        }

        // Unquoted identifiers fold to lowercase (libpg_query); quote in SQL to preserve case.
        private static string ExtractFieldName(Node node, string fromAlias = null)
        {
            if (node?.ColumnRef != null && node.ColumnRef.Fields != null)
            {
                var fields = node.ColumnRef.Fields;
                if (fields.Count > 0)
                {
                    var parts = new string[fields.Count];
                    for (int i = 0; i < fields.Count; i++)
                    {
                        var s = GetStringValue(fields[i]);
                        if (s == null)
                            throw new NotSupportedException("Unsupported field reference");
                        parts[i] = s;
                    }
                    return StripAliasPrefix(string.Join('.', parts), fromAlias);
                }
            }

            return null;
        }

        private static string StripAliasPrefix(string field, string fromAlias)
        {
            if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(fromAlias))
                return field;

            var prefix = fromAlias + ".";
            if (field.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return field[prefix.Length..];

            return field;
        }

        private static string GetStringValue(Node node)
        {
            return node.String?.Sval;
        }
    }
}
