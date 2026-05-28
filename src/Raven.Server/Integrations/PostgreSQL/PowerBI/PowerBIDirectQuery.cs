using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PgSqlParser;
using Raven.Server.Documents;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Queries.AST;
using Raven.Server.Logging;
using Sparrow;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Sparrow.Logging;
using Sparrow.Server.Logging;
using JsAst = Acornima.Ast;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    // Handles PowerBI's DirectQuery mode — outer SQL wrapping inner RQL — by recognizing the
    // wrapper shape, classifying it (grouped aggregate or simple projection), and rewriting the
    // resolved Raven.Server.Documents.Queries.AST.Query in place. The class focuses on the
    // PgQuery lifecycle plus the AST rewriters; recognition + shape classification live in
    // PowerBIWrapperRecognizer / PowerBIShapeClassifier (extracted in the P-C refactor), and the
    // RQL is rendered by the canonical StringQueryVisitor via Query.ToString() (P-D / P-E).
    public sealed class PowerBIDirectQuery : PowerBIRqlQuery
    {
        private static readonly RavenLogger Logger = RavenLogManager.Instance.GetLoggerForServer<PowerBIDirectQuery>();

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

                if (PowerBIWrapperRecognizer.TryNormalize(selectStmt, out var wrapper) == false)
                    return false;

                // Apply WHEREs found at intermediate wrapper levels to the resolved inner query
                // BEFORE either rewriter runs. PowerBI's DirectQuery routinely plants user filters
                // inside nested wrappers (e.g. between the null-ordering CASE helpers and the
                // distinct-grouping level), not at the outermost SELECT. Without this merge those
                // filters get silently dropped and the query returns the whole collection.
                //
                // Both rewriters consume inner.ResolvedQuery.Where: the grouped-aggregate path
                // preserves it via ShallowCopy, and the simple-direct path AND-merges it with the
                // outer WHERE downstream. So we just need to populate it here once.
                //
                // IMPORTANT: we filter out WHEREs that reference aggregate-output aliases (e.g.
                // `where not "_"."a0" is null` where `a0` came from `sum(Freight) as a0`). PowerBI
                // emits those as post-grouping null-guards; RQL gets the same effect implicitly
                // from GROUP BY semantics. Trying to translate them targets `a0` against the inner
                // Orders query, which has no such field, and RavenDB rejects the resulting RQL
                // ("Field 'a0' is neither an aggregation operation nor part of the group by key").
                if (wrapper.IntermediateWheres is { Count: > 0 })
                {
                    var aggregateOutputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (wrapper.Aggregates != null)
                    {
                        foreach (var agg in wrapper.Aggregates)
                        {
                            if (string.IsNullOrEmpty(agg.OutputColumn) == false)
                                aggregateOutputNames.Add(agg.OutputColumn);
                        }
                    }

                    var iq = inner.ResolvedQuery;
                    if (iq.From.Alias == null)
                        iq.From.Alias = "_doc";

                    foreach (var iw in wrapper.IntermediateWheres)
                    {
                        if (WhereClauseReferencesAnyColumn(iw.WhereClause, aggregateOutputNames))
                            continue;

                        if (PowerBIOuterWhereTranslator.TryTranslateWhere(iw.WhereClause, iw.WrapperAlias, iq.From.Alias, out var whereExpression) == false)
                            return false;

                        iq.Where = iq.Where == null
                            ? whereExpression
                            : new BinaryExpression(iq.Where, whereExpression, OperatorType.And);
                    }
                }

                if (PowerBIShapeClassifier.TryBuildGroupedAggregateShape(wrapper, out var aggregateShape))
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

                if (PowerBIShapeClassifier.TryBuildDirectQueryShape(wrapper, out var shape) == false)
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

        // AST-based grouped-aggregate emission (Step P-D). The previous StringBuilder version
        // hand-rolled every RQL fragment ("group by ", ", ", " as long", "limit 0, "); this one
        // mutates the resolved Raven.Server.Documents.Queries.AST.Query in place and lets the
        // canonical StringQueryVisitor (via Query.ToString()) produce the final RQL. Same
        // observable behaviour — same group-key + aggregate projection, same ORDER BY semantics
        // (Long for count, Double for sum), same Limit. When a SQL shape can't be represented in
        // the AST we return null and surface a clear failure to the dispatch chain.
        private static string RewriteGroupedAggregateRql(Documents.Queries.AST.Query q, GroupedAggregateShape shape)
        {
            if (q == null || shape == null)
                return null;
            if (q.From.From == null)
                return null;
            if (shape.Aggregates is not { Count: > 0 } aggregates)
                return null;
            if (shape.GroupByFields is not { Count: > 0 } groupByFields)
                return null;

            // Validate every identifier the same way the StringBuilder emitter used to —
            // FormatRqlIdentifier is strict (ASCII plain only). Returns null to drop the shape so
            // a query like `count("Field With Space") as a0` falls through instead of producing
            // RQL the downstream parser won't accept.
            foreach (var f in groupByFields)
                if (FormatRqlIdentifier(f) == null)
                    return null;
            foreach (var agg in aggregates)
            {
                if (FormatRqlIdentifier(agg.FieldName) == null) return null;
                if (FormatRqlIdentifier(agg.OutputColumn) == null) return null;
            }

            // Mutate a shallow copy so the inner query AST passed in isn't disturbed.
            var core = q.ShallowCopy();
            core.IsDistinct = false;
            core.Filter = null;
            core.FilterLimit = null;
            core.Load = null;
            core.Include = null;
            core.CachedOrderBy = null;
            core.Offset = null;
            core.SelectFunctionBody = default;

            // GROUP BY: one FieldExpression per key, no aliases.
            core.GroupBy = new List<(QueryExpression Expression, StringSegment? Alias)>(groupByFields.Count);
            foreach (var f in groupByFields)
                core.GroupBy.Add((MakeFieldExpression(f), null));

            // SELECT: group keys first, then each aggregate as `<fn>(<field>) AS <alias>` (or
            // `count() AS <alias>` — Raven's grouped RQL is row-count, never field-count).
            core.Select = new List<(QueryExpression Expression, StringSegment? Alias)>(groupByFields.Count + aggregates.Count);
            foreach (var f in groupByFields)
                core.Select.Add((MakeFieldExpression(f), null));
            foreach (var agg in aggregates)
            {
                var args = IsCountFunction(agg.FunctionName)
                    ? new List<QueryExpression>()
                    : new List<QueryExpression> { MakeFieldExpression(agg.FieldName) };
                core.Select.Add((new MethodExpression(agg.FunctionName, args), new StringSegment(agg.OutputColumn)));
            }

            // ORDER BY: resolve each entry against the group-key set or the aggregate-output set.
            if (TryBuildOrderByAst(shape, out var orderBy) == false)
                return null;
            core.OrderBy = orderBy; // null when shape has no ORDER BY

            // LIMIT: keep PowerBI's 1,000,001 default when the wrapper didn't supply one.
            core.Limit = new ValueExpression(shape.Limit.ToString(CultureInfo.InvariantCulture), ValueTokenType.Long);

            // q.Where is preserved as-is (already populated during inner resolution + outer
            // WHERE merge upstream). The classifier guarantees the outer WHERE is just the
            // `<agg-output> IS NOT NULL` post-filter, which RQL gets implicitly via GROUP BY.

            var rql = core.ToString();
            return string.IsNullOrWhiteSpace(rql) ? null : rql;
        }

        private static FieldExpression MakeFieldExpression(string name)
            => new(new List<StringSegment> { new(name) });

        // Walks a WHERE-clause AST and returns true if any ColumnRef (anywhere — wrapped in BoolExpr,
        // A_Expr, NullTest, TypeCast, RelabelType) names a column in the given set. Used to detect
        // intermediate WHEREs that reference aggregate-output aliases like `a0` — those are post-
        // grouping null-guards that RQL handles implicitly, and translating them against the inner
        // (pre-aggregation) query produces "field is neither aggregation nor group key" errors.
        private static bool WhereClauseReferencesAnyColumn(Node node, HashSet<string> columnNames)
        {
            if (node == null || columnNames is not { Count: > 0 })
                return false;

            return Walk(node);

            bool Walk(Node n)
            {
                if (n == null)
                    return false;

                if (n.ColumnRef?.Fields is { Count: > 0 } fields)
                {
                    var last = fields[^1]?.String?.Sval;
                    if (last != null && columnNames.Contains(last))
                        return true;
                }

                if (n.BoolExpr?.Args != null)
                {
                    foreach (var arg in n.BoolExpr.Args)
                        if (Walk(arg)) return true;
                }

                if (n.AExpr != null)
                {
                    if (Walk(n.AExpr.Lexpr)) return true;
                    if (Walk(n.AExpr.Rexpr)) return true;
                }

                if (n.NullTest?.Arg != null && Walk(n.NullTest.Arg))
                    return true;

                if (n.TypeCast?.Arg != null && Walk(n.TypeCast.Arg))
                    return true;

                if (n.RelabelType?.Arg != null && Walk(n.RelabelType.Arg))
                    return true;

                return false;
            }
        }

        private static bool TryBuildOrderByAst(GroupedAggregateShape shape, out List<(QueryExpression Expression, OrderByFieldType FieldType, bool Ascending)> orderBy)
        {
            orderBy = null;

            if (shape.OrderByCols == null || shape.OrderByDescFlags == null)
                return false;

            if (shape.OrderByCols.Count != shape.OrderByDescFlags.Count)
                return false;

            if (shape.OrderByCols.Count == 0)
                return true; // null OrderBy means "no ORDER BY" — visitor skips it.

            var list = new List<(QueryExpression, OrderByFieldType, bool)>(capacity: shape.OrderByCols.Count);
            for (int i = 0; i < shape.OrderByCols.Count; i++)
            {
                var col = shape.OrderByCols[i];
                var ascending = shape.OrderByDescFlags[i] == false;

                // Aggregate-output alias? Use the alias as a field reference and tag with the
                // numeric sort type matching the aggregate's RQL output kind (Long for count,
                // Double for sum) — preserves the StringBuilder emitter's `as long` / `as double`.
                int aggIndex = -1;
                for (int a = 0; a < shape.Aggregates.Count; a++)
                {
                    if (string.Equals(col, shape.Aggregates[a].OutputColumn, StringComparison.OrdinalIgnoreCase))
                    {
                        aggIndex = a;
                        break;
                    }
                }
                if (aggIndex >= 0)
                {
                    var agg = shape.Aggregates[aggIndex];
                    var fieldType = IsCountFunction(agg.FunctionName) ? OrderByFieldType.Long : OrderByFieldType.Double;
                    list.Add((MakeFieldExpression(agg.OutputColumn), fieldType, ascending));
                    continue;
                }

                // Otherwise must be a group-key match (case-insensitive).
                var foundGroupKey = false;
                foreach (var gb in shape.GroupByFields)
                {
                    if (string.Equals(col, gb, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add((MakeFieldExpression(gb), OrderByFieldType.Implicit, ascending));
                        foundGroupKey = true;
                        break;
                    }
                }
                if (foundGroupKey == false)
                    return false;
            }

            orderBy = list;
            return true;
        }

        // IsValidRqlSelect kept here — it's an emitter-side validation, not recognition.
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

        private enum InnerProjectionMode
        {
            Rebuild,
            PreserveExact,
            PreserveWithExtras
        }

        // AST-based simple direct-query emission (Step P-E). RQL's `select { … }` object
        // projection is an opaque string in the Query AST (SelectFunctionBody), so the rebuild
        // path still hand-builds that body text — but the surrounding clauses (Limit, etc.) are
        // set through the AST and rendered by the canonical StringQueryVisitor. The previous
        // version called Query.ToString() to get a prefix and then concatenated `\nselect { … }\n
        // limit 0, N` by hand; this one threads the rebuilt body through SelectFunctionBody so the
        // visitor emits everything in canonical order.
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
            core.CachedOrderBy = null;
            core.Offset = null;
            core.Select = null; // we always emit via SelectFunctionBody for object projections.

            switch (mode)
            {
                case InnerProjectionMode.Rebuild:
                    if (TryBuildObjectProjectionBody(projectionCols, q.From.Alias?.Value, out var newBody) == false)
                        return null;
                    core.SelectFunctionBody = (newBody, null, null);
                    break;

                case InnerProjectionMode.PreserveExact:
                    // Leave core.SelectFunctionBody as shallow-copied from q.
                    break;

                case InnerProjectionMode.PreserveWithExtras:
                    var aliasText = q.From.Alias?.Value ?? "_doc";
                    if (TryExtendInnerProjectionBody(q.SelectFunctionBody.FunctionText, extras, aliasText, out var extendedBody) == false)
                        return null;
                    core.SelectFunctionBody = (extendedBody, null, null);
                    break;
            }

            if (limit >= 0)
                core.Limit = new ValueExpression(limit.ToString(CultureInfo.InvariantCulture), ValueTokenType.Long);

            var rql = core.ToString();
            return string.IsNullOrWhiteSpace(rql) ? null : rql;
        }

        // Builds the body of a `select { … }` object projection. Returns false when any column
        // resists RQL identifier formatting — caller drops the shape.
        private static bool TryBuildObjectProjectionBody(IReadOnlyList<string> projectionCols, string fromAlias, out string body)
        {
            body = null;

            var selectParts = new List<string>(capacity: projectionCols.Count);
            for (int i = 0; i < projectionCols.Count; i++)
            {
                var colName = projectionCols[i];
                if (string.IsNullOrWhiteSpace(colName))
                    return false;

                if (string.Equals(colName, "json()", StringComparison.OrdinalIgnoreCase))
                {
                    selectParts.Add($"\"json()\": {fromAlias}");
                    continue;
                }

                if (string.Equals(colName, "id()", StringComparison.OrdinalIgnoreCase))
                {
                    selectParts.Add($"\"id()\": id({fromAlias})");
                    continue;
                }

                var selectField = FormatRqlObjectFieldIdentifier(colName);
                if (selectField == null)
                    return false;

                var expr = BuildFieldExpression(colName, fromAlias);
                if (expr == null)
                    return false;

                selectParts.Add($"{selectField}: {expr}");
            }

            body = "{ " + string.Join(", ", selectParts) + " }";
            return true;
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
