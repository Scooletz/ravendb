using System;
using System.Collections.Generic;
using System.Globalization;
using PgSqlParser;
using Sparrow;
using Raven.Server.Integrations.PostgreSQL.Translation;
using Raven.Server.Integrations.PostgreSQL.Types;
using Raven.Server.Documents;
using Raven.Server.Documents.Queries.AST;
using Raven.Server.Logging;
using Sparrow.Logging;
using Sparrow.Server.Logging;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public static class PowerBIFetchQuery
    {
        private static readonly RavenLogger Logger = RavenLogManager.Instance.GetLoggerForServer(typeof(PowerBIFetchQuery));

        public static bool TryParse(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            if (TryParseSimpleTableFetchViaAst(queryText, parametersDataTypes, documentDatabase, out pgQuery))
                return true;

            if (TryParseWrappedRqlFetchViaAst(queryText, parametersDataTypes, documentDatabase, out pgQuery))
                return true;

            pgQuery = null;
            return false;
        }

        private static bool TryParseWrappedRqlFetchViaAst(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            pgQuery = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            try
            {
                var inner = PowerBIInnerRqlExtractor.TryExtractAndResolve(queryText);
                if (inner == null)
                    return false;

                var selectStmt = inner.SanitizedSelectStmt;
                if (selectStmt == null)
                    return false;

                int? limit = null;
                if (selectStmt.LimitCount != null)
                {
                    if (PgSqlAstHelpers.TryReadNonNegativeIntConst(selectStmt.LimitCount, out var l))
                        limit = l;
                }

                var query = inner.ResolvedQuery;

                Dictionary<string, ReplaceColumnValue> allReplaces = null;
                List<Dictionary<string, ReplaceColumnValue>> wrapperReplaces = null;
                List<(Node WhereClause, string WrapperAlias)> deferredWheres = null;

                var currentSelect = selectStmt;
                var isOuterMost = true;

                while (true)
                {
                    if (IsWrapperRangeSubselectSelect(currentSelect, out var wrapperAlias, out var nextSelect) == false)
                    {
                        if (isOuterMost)
                            return false;

                        break;
                    }

                    if (TryExtractPowerBiReplaceColumns(currentSelect, wrapperAlias, out var levelReplaces) == false)
                        return false;

                    if (levelReplaces != null)
                    {
                        wrapperReplaces ??= new List<Dictionary<string, ReplaceColumnValue>>();
                        wrapperReplaces.Add(levelReplaces);
                    }

                    // Defer WHERE application until after we've walked to the innermost SELECT
                    // and collected its aggregate-output aliases. PowerBI commonly emits an
                    // outer `where not "_"."a0" is null` guard on top of an inner
                    // `select Freight, sum(Freight) as "a0" ... group by Freight` — translating
                    // that WHERE against the inner query produces invalid RQL (`a0` isn't a
                    // field of Orders) and the engine throws mid-response with
                    // `Exception while reading from stream`. We need the alias set first to
                    // know which WHEREs to drop, but we can only see it after the walk.
                    if (currentSelect.WhereClause != null)
                    {
                        deferredWheres ??= new List<(Node, string)>();
                        deferredWheres.Add((currentSelect.WhereClause, wrapperAlias));
                    }

                    if (nextSelect == null)
                        break;

                    currentSelect = nextSelect;
                    isOuterMost = false;
                }

                if (wrapperReplaces != null)
                {
                    for (int i = wrapperReplaces.Count - 1; i >= 0; i--)
                    {
                        allReplaces ??= new Dictionary<string, ReplaceColumnValue>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kvp in wrapperReplaces[i])
                            allReplaces[kvp.Key] = kvp.Value;
                    }
                }

                // `currentSelect` is the SANITIZED innermost — PowerBIInnerRqlExtractor replaces
                // the real inner text with `select 1` so pgsqlparser can parse the outer wrapper
                // structure (the real inner is often RQL or shapes pgsqlparser can't handle).
                // So we can't get aggregate aliases from currentSelect.TargetList — it just has
                // `select 1`. Re-parse the raw inner text instead. If the inner is SQL, this
                // surfaces its aggregate-output aliases; if the inner is RQL, parsing fails and
                // we return an empty set (RQL doesn't preserve SQL aliases anyway, so there's
                // nothing for the outer WHERE to reference by alias).
                var aggregateOutputAliases = CollectAggregateAliasesFromInnerSql(inner.InnerText);

                if (deferredWheres != null)
                {
                    foreach (var (whereClause, wrapperAlias) in deferredWheres)
                    {
                        if (PowerBIDirectQuery.WhereClauseReferencesAnyColumn(whereClause, aggregateOutputAliases))
                            continue;

                        if (PowerBIOuterWhereTranslator.TryTranslateWhere(whereClause, wrapperAlias, query.From.Alias, out var whereExpression) == false)
                            return false;

                        query.Where = query.Where == null
                            ? whereExpression
                            : new BinaryExpression(query.Where, whereExpression, OperatorType.And);
                    }
                }

                // PowerBI's row-preview / drill-down queries often decorate the outermost SELECT
                // with constant markers like `1 as "c0"` so the client can count back a known
                // fixed shape. The inner RQL we resolved above never sees those — they live in
                // the outermost wrapper's projection list — so the engine returns one column
                // fewer than PowerBI expects (`Field count mismatch when mapping column types.
                // N vs N-1`). Collect them here and pass to PowerBIRqlQuery; it appends them as
                // synthetic columns AFTER the json synthetic-append (so the wire-order matches
                // PowerBI's SQL: id, user cols, json, c0) and types them per PG's literal
                // inference rules (e.g. unadorned `1` is int4, not int8 — sending int8 trips
                // PowerBI's OLE DB provider with DISP_E_TYPEMISMATCH).
                var constProjections = TryCollectOuterConstProjections(selectStmt);

                var newRql = query.ToString();

                // Mirror the dispatch rule from TryParseSimpleTableFetchViaAst: PowerBI's
                // RowDescription expectations come from its OUTERMOST projection list. When the
                // outermost asks for narrow user columns only (no id()/json() references) the
                // synthetic id+json appended by PowerBIRqlQuery widens the response past the
                // requested set — PowerBI's mashup engine then bails with `Field count mismatch
                // when mapping column types. N vs N+1`. This commonly happens for post-aggregate
                // wrappers like `select "_"."Freight", "_"."a0" from (group by/aggregate) "_"
                // where not "_"."a0" is null` — outer asks for 2 cols, base would emit 3.
                //
                // Stay on PowerBIRqlQuery when its extra plumbing is actually doing work —
                // wrapper-level REPLACE() column rewrites (allReplaces) or constant-marker
                // outer projections (constProjections) — both of which PgSqlTranslatedRqlQuery
                // doesn't implement.
                if (WantsPowerBISyntheticColumns(selectStmt) || allReplaces != null || constProjections != null)
                {
                    pgQuery = new PowerBIRqlQuery(newRql, parametersDataTypes, documentDatabase, allReplaces, limit: limit, constProjections: constProjections);
                }
                else
                {
                    pgQuery = new PgSqlTranslatedRqlQuery(newRql, parametersDataTypes, documentDatabase, limit: limit);
                }
                return true;
            }
            catch (Exception e)
            {
                if (Logger.IsDebugEnabled)
                    Logger.Debug($"{nameof(PowerBIFetchQuery)}.{nameof(TryParseWrappedRqlFetchViaAst)} rejected query: {e.Message}");
                pgQuery = null;
                return false;
            }

            static bool IsWrapperRangeSubselectSelect(SelectStmt s, out string alias, out SelectStmt nextSelect)
            {
                alias = null;
                nextSelect = null;

                if (s.FromClause is not { Count: 1 })
                    return false;

                var fromItem = s.FromClause[0];
                if (fromItem?.RangeSubselect == null)
                    return false;

                if (fromItem.RangeVar != null || fromItem.JoinExpr != null)
                    return false;

                var rss = fromItem.RangeSubselect;
                alias = rss.Alias?.Aliasname;
                if (PgSqlAstHelpers.IsPowerBiWrapperAlias(alias) == false)
                    return false;

                nextSelect = rss.Subquery?.SelectStmt;
                return true;
            }
        }

        // Parses the raw inner text as SQL and collects aggregate-output aliases — projections
        // of the form `<aggregate-func>(<args>) AS <alias>`. Outer wrapper levels often
        // reference these aliases in their WHERE clauses (PowerBI's standard post-aggregation
        // null guard, e.g. `where not "_"."a0" is null`); those WHEREs must be dropped because
        // the RQL we emit already encodes the aggregation — the alias doesn't survive as a
        // field of the underlying collection, and trying to translate the WHERE against the
        // RQL produces an invalid `WHERE a0 != null` that explodes mid-response with
        // `Exception while reading from stream`.
        //
        // If the inner text isn't parseable as SQL (e.g. it's RQL embedded inside the wrapper),
        // returns an empty set — RQL doesn't carry SQL aliases anyway, so the outer WHERE
        // wouldn't be referencing them by alias.
        private static HashSet<string> CollectAggregateAliasesFromInnerSql(string innerText)
        {
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(innerText))
                return aliases;

            try
            {
                var parseResult = Parser.Parse(innerText);
                if (parseResult.IsSuccess == false || parseResult.Value?.Stmts is not { Count: 1 })
                    return aliases;

                var innerSelect = parseResult.Value.Stmts[0]?.Stmt?.SelectStmt;
                if (innerSelect?.TargetList == null)
                    return aliases;

                foreach (var t in innerSelect.TargetList)
                {
                    var resTarget = t?.ResTarget;
                    if (resTarget?.Val?.FuncCall == null)
                        continue;
                    if (string.IsNullOrWhiteSpace(resTarget.Name))
                        continue;
                    aliases.Add(resTarget.Name);
                }
            }
            catch
            {
                // Inner text isn't SQL (probably RQL or some malformed shape) — leave the
                // alias set empty. Outer WHEREs that reference aggregate aliases via this
                // path won't occur because the alias structure only exists in SQL.
            }

            return aliases;
        }

        // Scans the outermost SELECT's TargetList for `<literal> as <alias>` projections —
        // typically `1 as "c0"` from PowerBI's row-preview shape — and packages them as
        // ConstProjection descriptors. PowerBIRqlQuery applies them as synthetic columns
        // appended AFTER the auto-included json column (matching PowerBI's expected wire
        // order: id, user cols, json, c0) and uses the PG-idiomatic type for each literal
        // (e.g. unadorned `1` is int4, not int8 — PowerBI's OLE DB provider expects int4
        // from parsing its own SQL and rejects int8 with DISP_E_TYPEMISMATCH).
        private static List<ConstProjection> TryCollectOuterConstProjections(SelectStmt outermost)
        {
            var targets = outermost?.TargetList;
            if (targets == null || targets.Count == 0)
                return null;

            List<ConstProjection> constProjections = null;

            foreach (var t in targets)
            {
                var resTarget = t?.ResTarget;
                var aConst = resTarget?.Val?.AConst;
                if (aConst == null)
                    continue;

                if (TryBuildConstProjection(aConst, resTarget.Name, out var cp) == false)
                    continue;

                constProjections ??= new List<ConstProjection>();
                constProjections.Add(cp);
            }

            return constProjections;
        }

        // Maps the three concrete AConst kinds plus Boolval and SQL NULL to typed wire values.
        // Integer literals are int4 — that's PG's default inference for an unadorned `1` token
        // and what PowerBI's OLE DB type-mapping expects. Float literals are float8 (numeric
        // promotion at parse time is rare for what PowerBI emits). Strings are text. Booleans
        // are bool. All-null components emit a NULL value in the synthetic column.
        private static bool TryBuildConstProjection(A_Const c, string alias, out ConstProjection projection)
        {
            projection = null;
            if (c == null || string.IsNullOrWhiteSpace(alias))
                return false;

            if (c.Ival != null)
            {
                // PG types `1` as int4 — narrow long to int. Out-of-range silently wraps,
                // which is fine for PowerBI's row-preview markers (always small positive ints).
                projection = new ConstProjection(alias, PgInt4.Default, (int)c.Ival.Ival);
                return true;
            }

            if (c.Fval != null && string.IsNullOrEmpty(c.Fval.Fval) == false &&
                double.TryParse(c.Fval.Fval, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
            {
                projection = new ConstProjection(alias, PgFloat8.Default, dv);
                return true;
            }

            if (c.Sval != null && c.Sval.Sval != null)
            {
                projection = new ConstProjection(alias, PgText.Default, c.Sval.Sval);
                return true;
            }

            if (c.Boolval != null)
            {
                projection = new ConstProjection(alias, PgBool.Default, c.Boolval.Boolval);
                return true;
            }

            // All-null components → SQL NULL. The synthetic column will encode as wire NULL
            // (no bytes, length prefix = -1) regardless of declared type.
            if (c.Ival == null && c.Fval == null && c.Sval == null && c.Boolval == null)
            {
                projection = new ConstProjection(alias, PgText.Default, null);
                return true;
            }

            return false;
        }

        private static bool TryExtractPowerBiReplaceColumns(SelectStmt selectStmt, string outerAlias, out Dictionary<string, ReplaceColumnValue> replaces)
        {
            replaces = null;

            var targets = selectStmt?.TargetList;
            if (targets == null || targets.Count == 0)
                return true;

            foreach (var t in targets)
            {
                var resTarget = t?.ResTarget;
                var funcCall = resTarget?.Val?.FuncCall;
                if (funcCall == null)
                    continue;

                var funcName = funcCall.Funcname is { Count: > 0 }
                    ? funcCall.Funcname[0].String?.Sval
                    : null;

                if (string.Equals(funcName, "replace", StringComparison.OrdinalIgnoreCase) == false)
                    return false;

                var dstColumn = resTarget.Name;
                if (string.IsNullOrWhiteSpace(dstColumn))
                    return false;

                if (funcCall.Args is not { Count: 3 })
                    return false;

                var arg0 = funcCall.Args[0];
                if (arg0?.ColumnRef?.Fields is not { Count: 2 } fields)
                    return false;

                var arg0Alias = fields[0].String?.Sval;
                if (string.IsNullOrWhiteSpace(arg0Alias) || string.Equals(arg0Alias, outerAlias, StringComparison.OrdinalIgnoreCase) == false)
                    return false;

                var srcColumn = fields[1].String?.Sval;
                if (string.IsNullOrWhiteSpace(srcColumn))
                    return false;

                var oldValue = funcCall.Args[1]?.AConst?.Sval?.Sval;
                var newValue = funcCall.Args[2]?.AConst?.Sval?.Sval;

                if (oldValue == null || newValue == null)
                    return false;

                replaces ??= new Dictionary<string, ReplaceColumnValue>(StringComparer.OrdinalIgnoreCase);
                replaces[srcColumn] = new ReplaceColumnValue
                {
                    SrcColumnName = srcColumn,
                    DstColumnName = dstColumn,
                    OldValue = oldValue,
                    NewValue = newValue
                };
            }

            return true;
        }

        private static bool TryParseSimpleTableFetchViaAst(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            pgQuery = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            if (TryGetSimplePublicRangeVarSelect(queryText, out var selectStmt) == false)
                return false;

            if (PgSqlToRqlTranslator.TryParse(queryText, parametersDataTypes, documentDatabase, out var rql) == false)
                return false;

            // PowerBI's RowDescription expectations must match exactly what its SQL projected —
            // any extra columns the server tacks on widen the response past the requested set and
            // PowerBI's mashup engine breaks (either raises `Field count mismatch when mapping
            // column types. N vs M` or, more subtly, dies inside the column-type mapper with
            // `Nullable object must have a value` and drops the socket).
            //
            // Two shapes want PowerBIRqlQuery's auto-added id() + json() synthetic columns:
            //   (1) `SELECT *` — PG semantics include every column the source has, which for
            //       PowerBI Import shapes means the synthetics too.
            //   (2) An explicit projection that literally names `id()` or `json()` — that's
            //       PowerBI's standard "fetch all visible rows" shape; the synthetic columns are
            //       part of the requested set, not server-side extras.
            // Every other shape — narrow projections, DISTINCT, GROUP BY — is a query where
            // PowerBI named exactly which columns it wants and expects the response to match
            // column-for-column. PK metadata (information_schema.table_constraints +
            // key_column_usage) tells PowerBI which logical column is the row identity, so
            // omitting the synthetic id() from a narrow projection doesn't break row-substitution
            // for tables PowerBI knows the PK of — that's already declared in metadata.
            var wantsSyntheticColumns = WantsPowerBISyntheticColumns(selectStmt);

            pgQuery = wantsSyntheticColumns
                ? new PowerBIRqlQuery(rql, parametersDataTypes, documentDatabase, replaces: null, limit: null)
                : new PgSqlTranslatedRqlQuery(rql, parametersDataTypes, documentDatabase);
            return true;
        }

        // True for shapes where PowerBI expects id() and json() in the RowDescription — either
        // `SELECT *` (implicit "all columns") or an explicit projection that names id() / json().
        // False for narrow projections (user columns only, DISTINCT, GROUP BY) where PowerBI
        // cares only about the exact columns it asked for.
        private static bool WantsPowerBISyntheticColumns(SelectStmt selectStmt)
        {
            var targets = selectStmt.TargetList;
            if (targets == null || targets.Count == 0)
                return true; // No target list → SELECT *-equivalent → wants everything.

            // pgsqlparser models `SELECT *` as a single ColumnRef whose first (and only) field
            // is an A_Star node. Match that shape so we don't strip synthetics for the implicit
            // wildcard.
            if (targets.Count == 1)
            {
                var only = targets[0]?.ResTarget?.Val;
                var fields = only?.ColumnRef?.Fields;
                if (fields is { Count: 1 } && fields[0]?.AStar != null)
                    return true;
            }

            // Explicit projection: scan each target's column ref for `id()` or `json()` as the
            // final path segment. PowerBI's standard fetch shape always names them — anything
            // else (user columns only / slicer-distinct / aggregate output) deliberately omits
            // them, and we should mirror that to keep the RowDescription matched.
            foreach (var t in targets)
            {
                var col = t?.ResTarget?.Val?.ColumnRef;
                var colFields = col?.Fields;
                if (colFields == null || colFields.Count == 0)
                    continue;

                var last = colFields[colFields.Count - 1]?.String?.Sval;
                if (string.IsNullOrEmpty(last))
                    continue;

                // Accept both the new PG-idiomatic forms (`id`, `json`) and the legacy
                // parenthesised forms (`id()`, `json()`) that older PowerBI metadata caches
                // still send. Either signals "this is the PowerBI fetch shape — keep synthetics".
                if (PgSyntheticColumns.IsSyntheticColumn(last))
                    return true;
            }

            return false;
        }

        private static bool TryGetSimplePublicRangeVarSelect(string sql, out SelectStmt selectStmt)
        {
            selectStmt = null;

            var parseResult = Parser.Parse(sql);
            if (parseResult.IsSuccess == false || parseResult.Value == null)
                return false;

            if (parseResult.Value.Stmts == null || parseResult.Value.Stmts.Count != 1)
                return false;

            var stmt = parseResult.Value.Stmts[0];
            var select = stmt?.Stmt?.SelectStmt;
            if (select == null)
                return false;

            if (select.FromClause is not { Count: 1 })
                return false;

            var rangeVar = select.FromClause[0]?.RangeVar;
            if (rangeVar == null)
                return false;

            if (string.Equals(rangeVar.Schemaname, "public", StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (string.IsNullOrWhiteSpace(rangeVar.Relname))
                return false;

            selectStmt = select;
            return true;
        }

    }
}
