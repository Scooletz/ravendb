using System;
using System.Collections.Generic;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Npgsql;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    /// <summary>
    /// Recognizes the metadata/initialization queries the Npgsql driver sends on startup and
    /// maps them to the canonical <see cref="PgTable"/> response that satisfies the driver.
    ///
    /// Covers three families:
    ///   - protocol probes: <c>version()</c>, <c>current_setting('max_index_keys')</c>, and
    ///     the two-statement batch that sends both;
    ///   - pg_catalog metadata: enum labels and composite-type field definitions;
    ///   - type-catalog loading: five distinct response shapes across Npgsql 3.2–5.x+.
    ///
    /// Each recognizer matches on structural features (source relations, projected column
    /// names, target count) rather than string shape, so whitespace, casing, comment style,
    /// ORDER BY, and — for many families — projection order are tolerated.
    /// </summary>
    internal static class NpgsqlQueryClassifier
    {
        private static readonly string[] OldFlatExpectedColumns =
        {
            "nspname", "typname", "oid", "typrelid", "typbasetype", "type", "elemoid", "ord"
        };

        /// <summary>
        /// Primary entry point, called by <see cref="HardcodedQueryClassifier"/> after parsing.
        /// Accepts 1 or 2 already-parsed SELECT statements (Npgsql never sends more).
        /// </summary>
        public static bool TryClassify(IReadOnlyList<SelectStmt> selects, out PgTable response)
        {
            response = Classify(selects);
            return response != null;
        }

        // ── Scope-filtered test helpers ───────────────────────────────────────────────────
        // Each helper parses its own input and only returns matches from its own family. A
        // query that is classifiable but belongs to a different family correctly returns
        // false, so tests can assert that classification boundaries hold.

        public static bool TryMatchSimpleQuery(string queryText, out PgTable result)
        {
            result = null;
            if (SelectStmtShape.TryParseSelectStatements(queryText, out var selects) == false)
                return false;

            // Two-statement form (version + current_setting) belongs to this family.
            if (selects.Count == 2)
            {
                result = ClassifyPair(selects);
                return result != null;
            }

            if (selects.Count != 1)
                return false;

            result = ClassifySimpleProbe(selects[0]);
            return result != null;
        }

        public static bool TryMatchMetadataQuery(string queryText, out PgTable result)
        {
            result = null;
            if (SelectStmtShape.TryParseSingleSelect(queryText, out var s) == false)
                return false;

            result = ClassifyPgCatalogMetadata(s);
            return result != null;
        }

        public static bool TryMatchTypesQuery(string queryText, out PgTable result)
        {
            result = null;
            if (SelectStmtShape.TryParseSingleSelect(queryText, out var s) == false)
                return false;

            result = ClassifyTypeCatalog(s);
            return result != null;
        }

        // ── Dispatch ──────────────────────────────────────────────────────────────────────

        private static PgTable Classify(IReadOnlyList<SelectStmt> selects)
        {
            if (selects == null || selects.Count == 0)
                return null;

            if (selects.Count == 2)
                return ClassifyPair(selects);

            if (selects.Count != 1 || selects[0] == null)
                return null;

            var s = selects[0];
            return ClassifySimpleProbe(s)
                   ?? ClassifyPgCatalogMetadata(s)
                   ?? ClassifyTypeCatalog(s);
        }

        private static PgTable ClassifyPair(IReadOnlyList<SelectStmt> selects)
        {
            return IsServerVersionProbe(selects[0]) && IsMaxIndexKeysProbe(selects[1])
                ? NpgsqlConfig.VersionCurrentSettingResponse
                : null;
        }

        private static PgTable ClassifySimpleProbe(SelectStmt s)
        {
            if (IsServerVersionProbe(s))
                return NpgsqlConfig.VersionResponse;

            if (IsMaxIndexKeysProbe(s))
                return NpgsqlConfig.CurrentSettingResponse;

            return null;
        }

        private static PgTable ClassifyPgCatalogMetadata(SelectStmt s)
        {
            if (IsEnumTypeLabelsQuery(s))
                return NpgsqlConfig.EnumTypesResponse;

            if (IsCompositeTypeFieldsQuery(s))
                return NpgsqlConfig.CompositeTypesResponse;

            return null;
        }

        private static PgTable ClassifyTypeCatalog(SelectStmt s)
        {
            if (IsTypeCatalogModernNested(s))
                return NpgsqlConfig.Npgsql5TypesResponse;

            if (IsTypeCatalogMidFlat(s))
                return NpgsqlConfig.Npgsql4_1_2TypesResponse;

            if (IsTypeCatalogLegacyV3(s))
                return NpgsqlConfig.Npgsql3TypesResponse;

            if (IsTypeCatalogOldFlatWithPseudoArrays(s))
                return NpgsqlConfig.TypesResponse;

            if (IsTypeCatalogOldFlatWithoutPseudoArrays(s))
                return NpgsqlConfig.Npgsql4_0_0TypesResponse;

            return null;
        }

        // ── Simple probes ─────────────────────────────────────────────────────────────────

        private static bool IsServerVersionProbe(SelectStmt s)
        {
            if (SelectStmtShape.HasNoFromClause(s) == false)
                return false;

            return SelectStmtShape.IsSingleUnqualifiedFunctionCall(s, "version", expectedArgCount: 0, out _);
        }

        private static bool IsMaxIndexKeysProbe(SelectStmt s)
        {
            if (SelectStmtShape.HasNoFromClause(s) == false)
                return false;

            if (SelectStmtShape.IsSingleUnqualifiedFunctionCall(s, "current_setting", expectedArgCount: 1, out var funcCall) == false)
                return false;

            // Only the specific key Npgsql probes on startup is claimed.
            // Other current_setting(...) calls are legitimate application queries.
            var argValue = funcCall.Args[0].AConst?.Sval?.Sval;
            return string.Equals(argValue, "max_index_keys", StringComparison.OrdinalIgnoreCase);
        }

        // ── pg_catalog metadata ───────────────────────────────────────────────────────────

        // FROM references pg_enum + pg_type, projecting exactly {oid, enumlabel}.
        private static bool IsEnumTypeLabelsQuery(SelectStmt s)
        {
            if (SelectStmtShape.ReferencesTable(s, "pg_enum") == false)
                return false;

            if (SelectStmtShape.ReferencesTable(s, "pg_type") == false)
                return false;

            return SelectStmtShape.ProjectedNamesEqual(s, "oid", "enumlabel");
        }

        // FROM references pg_attribute + pg_class, projecting exactly {oid, attname, atttypid}.
        private static bool IsCompositeTypeFieldsQuery(SelectStmt s)
        {
            if (SelectStmtShape.ReferencesTable(s, "pg_attribute") == false)
                return false;

            if (SelectStmtShape.ReferencesTable(s, "pg_class") == false)
                return false;

            return SelectStmtShape.ProjectedNamesEqual(s, "oid", "attname", "atttypid");
        }

        // ── Type-catalog families ─────────────────────────────────────────────────────────

        // Npgsql 4.1.3–5.x+: outer SELECT projects 3 targets including a qualified wildcard
        // (e.g. typ_and_elem_type.*), and the FROM contains a subquery. This is the only
        // type-catalog family that uses a subquery in FROM.
        private static bool IsTypeCatalogModernNested(SelectStmt s)
        {
            if (s.TargetList is not { Count: 3 })
                return false;

            if (SelectStmtShape.HasWildcardTarget(s) == false)
                return false;

            return SelectStmtShape.ContainsSubselectInFrom(s);
        }

        // Npgsql 4.1.0–4.1.2: 7 projected targets, projects 'typelem' (other flat families
        // project 'elemoid'), and pg_type is in FROM.
        private static bool IsTypeCatalogMidFlat(SelectStmt s)
        {
            if (s.TargetList is not { Count: 7 })
                return false;

            if (SelectStmtShape.ReferencesTable(s, "pg_type") == false)
                return false;

            return SelectStmtShape.ProjectsName(s, "typelem");
        }

        // Npgsql 3.2.3–3.2.7: pg_type + pg_proc in FROM, pg_class absent — every 4.x flat
        // family joins pg_class, so its absence uniquely identifies this legacy shape.
        private static bool IsTypeCatalogLegacyV3(SelectStmt s)
        {
            if (s.TargetList is not { Count: 8 })
                return false;

            if (SelectStmtShape.ReferencesTable(s, "pg_type") == false)
                return false;

            if (SelectStmtShape.ReferencesTable(s, "pg_proc") == false)
                return false;

            if (SelectStmtShape.ReferencesTable(s, "pg_class"))
                return false;

            return HasAllExpectedOldFlatColumns(s);
        }

        // Npgsql 4.0.1–4.0.12: old flat shape, WHERE contains an array_recv branch that
        // includes the pseudo-type ('p') test.
        private static bool IsTypeCatalogOldFlatWithPseudoArrays(SelectStmt s)
        {
            if (IsOldFlatShape(s) == false)
                return false;

            return ArrayRecvBlockMentionsPseudoType(s, expected: true);
        }

        // Npgsql 4.0.0: old flat shape, WHERE contains an array_recv branch that does NOT
        // include the pseudo-type test — the branch was added in 4.0.1.
        private static bool IsTypeCatalogOldFlatWithoutPseudoArrays(SelectStmt s)
        {
            if (IsOldFlatShape(s) == false)
                return false;

            return ArrayRecvBlockMentionsPseudoType(s, expected: false);
        }

        private static bool IsOldFlatShape(SelectStmt s)
        {
            if (s.TargetList is not { Count: 8 })
                return false;

            if (SelectStmtShape.ReferencesTable(s, "pg_type") == false)
                return false;

            if (SelectStmtShape.ReferencesTable(s, "pg_proc") == false)
                return false;

            if (SelectStmtShape.ReferencesTable(s, "pg_class") == false)
                return false;

            return HasAllExpectedOldFlatColumns(s);
        }

        private static bool HasAllExpectedOldFlatColumns(SelectStmt s)
            => SelectStmtShape.ProjectedNamesContainAll(s, OldFlatExpectedColumns);

        private static bool ArrayRecvBlockMentionsPseudoType(SelectStmt s, bool expected)
        {
            if (SelectStmtShape.TryGetArrayRecvInnerOrBlock(s, out var innerOr) == false)
                return false;

            return SelectStmtShape.SubtreeContainsAExprRhsStringConstant(innerOr, "p") == expected;
        }
    }
}
