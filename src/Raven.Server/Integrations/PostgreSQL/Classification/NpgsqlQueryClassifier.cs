using System;
using System.Collections.Generic;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    /// <summary>
    /// Classifies Npgsql driver metadata/initialization intents. Covers:
    /// <list type="bullet">
    ///   <item>Simple protocol probes — <c>version()</c>, <c>current_setting('max_index_keys')</c>,
    ///     and the two-statement combined form.</item>
    ///   <item>pg_catalog metadata — enum and composite type definitions.</item>
    ///   <item>Type-catalog loading — five distinct response shapes covering Npgsql 3.2–5.x+.</item>
    /// </list>
    ///
    /// Public surface:
    /// <list type="bullet">
    ///   <item><see cref="TryClassify(IReadOnlyList{SelectStmt}, out MetadataIntent)"/> — primary
    ///     classification entry point; called by <see cref="HardcodedQueryClassifier"/> with
    ///     already-parsed statements so no re-parsing occurs in the main dispatch path.</item>
    ///   <item><see cref="TryMatchSimpleQuery"/>, <see cref="TryMatchMetadataQuery"/>,
    ///     <see cref="TryMatchTypesQuery"/> — scope-filtered convenience helpers used by tests.</item>
    /// </list>
    ///
    /// Each intent is identified by source-relation and projected-column features, so harmless
    /// variations in whitespace, casing, comment style, ORDER BY, and (for many families)
    /// projection order are all tolerated.
    /// </summary>
    internal static class NpgsqlQueryClassifier
    {
        // ── Primary classification entry point ────────────────────────────────────────────

        /// <summary>
        /// Classifies the Npgsql intent expressed by <paramref name="selects"/>. Handles both
        /// single-statement and the two-statement version+current_setting batch.
        /// Called by <see cref="HardcodedQueryClassifier"/> with already-parsed ASTs.
        /// </summary>
        public static bool TryClassify(IReadOnlyList<SelectStmt> selects, out MetadataIntent intent)
        {
            intent = default;

            if (selects == null || selects.Count == 0)
                return false;

            if (selects.Count == 2)
            {
                if (IsServerVersionProbe(selects[0]) && IsMaxIndexKeysProbe(selects[1]))
                {
                    intent = MetadataIntent.ServerVersionAndMaxIndexKeys;
                    return true;
                }
                return false;
            }

            if (selects.Count != 1)
                return false;

            var s = selects[0];
            if (s == null)
                return false;

            if (IsServerVersionProbe(s))      { intent = MetadataIntent.ServerVersion;       return true; }
            if (IsMaxIndexKeysProbe(s))        { intent = MetadataIntent.MaxIndexKeys;        return true; }
            if (IsEnumTypeLabelsQuery(s))      { intent = MetadataIntent.EnumTypeLabels;      return true; }
            if (IsCompositeTypeFieldsQuery(s)) { intent = MetadataIntent.CompositeTypeFields; return true; }

            if (IsTypeCatalogModernNested(s))            { intent = MetadataIntent.TypeCatalogModernNested;            return true; }
            if (IsTypeCatalogMidFlat(s))                 { intent = MetadataIntent.TypeCatalogMidFlat;                 return true; }
            if (IsTypeCatalogLegacyV3(s))                { intent = MetadataIntent.TypeCatalogLegacyV3;                return true; }
            if (IsTypeCatalogOldFlatWithPseudoArrays(s)) { intent = MetadataIntent.TypeCatalogOldFlatWithPseudoArrays; return true; }
            if (IsTypeCatalogOldFlatWithoutPseudoArrays(s)) { intent = MetadataIntent.TypeCatalogOldFlatWithoutPseudoArrays; return true; }

            return false;
        }

        // ── Scope-filtered test helpers ───────────────────────────────────────────────────
        // These parse their own input (intentional: they are convenience methods for tests,
        // not part of the main dispatch path). Each one only claims the intent subset it
        // corresponds to — a query classified as a different family correctly returns false.

        /// <summary>Tests: version(), current_setting('max_index_keys'), and their combined form.</summary>
        public static bool TryMatchSimpleQuery(string queryText, out PgTable result)
            => TryMatchScoped(queryText, out result, IsSimpleProbeIntent);

        /// <summary>Tests: enum-type labels and composite-type fields queries.</summary>
        public static bool TryMatchMetadataQuery(string queryText, out PgTable result)
            => TryMatchScoped(queryText, out result, IsPgCatalogMetadataIntent);

        /// <summary>Tests: all five type-catalog loading variants (Npgsql 3.2–5.x+).</summary>
        public static bool TryMatchTypesQuery(string queryText, out PgTable result)
            => TryMatchScoped(queryText, out result, IsTypeCatalogIntent);

        private static bool TryMatchScoped(string queryText, out PgTable result, Func<MetadataIntent, bool> inScope)
        {
            result = null;

            if (TryParseAndClassify(queryText, out var intent) == false)
                return false;

            if (inScope(intent) == false)
                return false;

            return intent.TryResolveToResponse(out result);
        }

        private static bool TryParseAndClassify(string queryText, out MetadataIntent intent)
        {
            intent = default;

            if (AstFeatures.TryParseSelectStatements(queryText, out var selects) == false)
                return false;

            return TryClassify(selects, out intent);
        }

        private static bool IsSimpleProbeIntent(MetadataIntent intent)
            => intent is MetadataIntent.ServerVersion
                      or MetadataIntent.MaxIndexKeys
                      or MetadataIntent.ServerVersionAndMaxIndexKeys;

        private static bool IsPgCatalogMetadataIntent(MetadataIntent intent)
            => intent is MetadataIntent.EnumTypeLabels
                      or MetadataIntent.CompositeTypeFields;

        private static bool IsTypeCatalogIntent(MetadataIntent intent)
            => intent is MetadataIntent.TypeCatalogModernNested
                      or MetadataIntent.TypeCatalogMidFlat
                      or MetadataIntent.TypeCatalogLegacyV3
                      or MetadataIntent.TypeCatalogOldFlatWithPseudoArrays
                      or MetadataIntent.TypeCatalogOldFlatWithoutPseudoArrays;

        // ── Simple probes ─────────────────────────────────────────────────────────────────

        private static bool IsServerVersionProbe(SelectStmt s)
        {
            if (AstFeatures.HasNoFromClause(s) == false)
                return false;

            return AstFeatures.IsSingleUnqualifiedFunctionCall(s, "version", expectedArgCount: 0, out _);
        }

        private static bool IsMaxIndexKeysProbe(SelectStmt s)
        {
            if (AstFeatures.HasNoFromClause(s) == false)
                return false;

            if (AstFeatures.IsSingleUnqualifiedFunctionCall(s, "current_setting", expectedArgCount: 1, out var funcCall) == false)
                return false;

            // Only the specific key Npgsql probes on startup is claimed.
            // Other current_setting(...) calls are legitimate application queries.
            var argValue = funcCall.Args[0].AConst?.Sval?.Sval;
            return string.Equals(argValue, "max_index_keys", StringComparison.OrdinalIgnoreCase);
        }

        // ── pg_catalog metadata — enum type labels ───────────────────────────────────────
        // Anchor: FROM references pg_enum and pg_type, projecting exactly {oid, enumlabel}.
        private static bool IsEnumTypeLabelsQuery(SelectStmt s)
        {
            if (AstFeatures.ReferencesTable(s, "pg_enum") == false)
                return false;

            if (AstFeatures.ReferencesTable(s, "pg_type") == false)
                return false;

            return AstFeatures.ProjectedNamesEqual(s, "oid", "enumlabel");
        }

        // ── pg_catalog metadata — composite type fields ──────────────────────────────────
        // Anchor: FROM references pg_attribute and pg_class, projecting exactly
        // {oid, attname, atttypid}.
        private static bool IsCompositeTypeFieldsQuery(SelectStmt s)
        {
            if (AstFeatures.ReferencesTable(s, "pg_attribute") == false)
                return false;

            if (AstFeatures.ReferencesTable(s, "pg_class") == false)
                return false;

            return AstFeatures.ProjectedNamesEqual(s, "oid", "attname", "atttypid");
        }

        // ── Type-catalog — modern nested (Npgsql 4.1.3–5.x+) ─────────────────────────────
        // Outer SELECT projects exactly 3 targets, one of which is a qualified wildcard
        // (e.g. typ_and_elem_type.*), and the FROM contains a subquery (RangeSubselect).
        // This is the only Npgsql type-catalog family that uses a subquery in FROM.
        private static bool IsTypeCatalogModernNested(SelectStmt s)
        {
            if (s.TargetList is not { Count: 3 })
                return false;

            if (AstFeatures.HasWildcardTarget(s) == false)
                return false;

            return AstFeatures.ContainsSubselectInFrom(s);
        }

        // ── Type-catalog — mid flat (Npgsql 4.1.0–4.1.2) ─────────────────────────────────
        // Anchors: exactly 7 projected targets, projects 'typelem' (other flat families
        // project 'elemoid'), and pg_type is in FROM.
        private static bool IsTypeCatalogMidFlat(SelectStmt s)
        {
            if (s.TargetList is not { Count: 7 })
                return false;

            if (AstFeatures.ReferencesTable(s, "pg_type") == false)
                return false;

            return AstFeatures.ProjectsName(s, "typelem");
        }

        // The expected column-name set shared by legacy-v3 + old-flat families.
        private static readonly string[] _oldFlatExpectedColumns =
        {
            "nspname", "typname", "oid", "typrelid", "typbasetype", "type", "elemoid", "ord"
        };

        // ── Type-catalog — legacy Npgsql 3 (3.2.3–3.2.7) ─────────────────────────────────
        // pg_type + pg_proc in FROM, pg_class ABSENT (all 4.x flat families join pg_class).
        private static bool IsTypeCatalogLegacyV3(SelectStmt s)
        {
            if (s.TargetList is not { Count: 8 })
                return false;

            if (AstFeatures.ReferencesTable(s, "pg_type") == false)
                return false;

            if (AstFeatures.ReferencesTable(s, "pg_proc") == false)
                return false;

            if (AstFeatures.ReferencesTable(s, "pg_class"))
                return false;

            return HasAllExpectedOldFlatColumns(s);
        }

        // ── Type-catalog — old flat with pseudo-array support (Npgsql 4.0.1–4.0.12) ──────
        private static bool IsTypeCatalogOldFlatWithPseudoArrays(SelectStmt s)
        {
            if (IsOldFlatCommon(s) == false)
                return false;

            return HasPseudoTypeBranchInArrayRecvBlock(s);
        }

        // ── Type-catalog — old flat without pseudo-array support (Npgsql 4.0.0 only) ─────
        private static bool IsTypeCatalogOldFlatWithoutPseudoArrays(SelectStmt s)
        {
            if (IsOldFlatCommon(s) == false)
                return false;

            return AstFeatures.TryGetArrayRecvInnerOrBlock(s, out var innerOr)
                && AstFeatures.SubtreeContainsAExprRhsStringConstant(innerOr, "p") == false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        private static bool IsOldFlatCommon(SelectStmt s)
        {
            if (s.TargetList is not { Count: 8 })
                return false;

            if (AstFeatures.ReferencesTable(s, "pg_type") == false)
                return false;

            if (AstFeatures.ReferencesTable(s, "pg_proc") == false)
                return false;

            if (AstFeatures.ReferencesTable(s, "pg_class") == false)
                return false;

            return HasAllExpectedOldFlatColumns(s);
        }

        private static bool HasAllExpectedOldFlatColumns(SelectStmt s)
            => AstFeatures.ProjectedNamesContainAll(s, _oldFlatExpectedColumns);

        private static bool HasPseudoTypeBranchInArrayRecvBlock(SelectStmt s)
            => AstFeatures.TryGetArrayRecvInnerOrBlock(s, out var innerOr)
               && AstFeatures.SubtreeContainsAExprRhsStringConstant(innerOr, "p");
    }
}
