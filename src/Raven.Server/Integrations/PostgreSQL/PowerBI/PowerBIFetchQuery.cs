using System;
using System.Collections.Generic;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Translation;
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

                    if (currentSelect.WhereClause != null)
                    {
                        if (PowerBIOuterWhereTranslator.TryTranslateWhere(currentSelect.WhereClause, wrapperAlias, query.From.Alias, out var whereExpression) == false)
                            return false;

                        query.Where = query.Where == null
                            ? whereExpression
                            : new BinaryExpression(query.Where, whereExpression, OperatorType.And);
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

                var newRql = query.ToString();

                pgQuery = new PowerBIRqlQuery(newRql, parametersDataTypes, documentDatabase, allReplaces, limit: limit);
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
