using System;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    /// <summary>
    /// Classifies PowerBI metadata-query intents from a parsed SQL statement. Each private
    /// classifier method answers a single "what metadata is this asking for?" question using
    /// semantic features (source relations, projected-name set, structural traits) — not
    /// positional or template matching.
    ///
    /// Public surface:
    /// <list type="bullet">
    ///   <item><see cref="TryClassify(SelectStmt, out MetadataIntent)"/> — used by
    ///     <see cref="HardcodedQueryClassifier"/> after it has parsed the SQL.</item>
    ///   <item><see cref="TryMatch(string, out PgTable)"/> — convenience for tests; parses
    ///     internally and resolves the intent to a response in one call.</item>
    /// </list>
    ///
    /// Intent → response mapping is centralized in
    /// <see cref="MetadataIntentExtensions.TryResolveToResponse"/>.
    /// </summary>
    internal static class PowerBIQueryClassifier
    {
        private const string InformationSchema = "information_schema";

        /// <summary>
        /// Classifies the PowerBI metadata intent expressed by <paramref name="selectStmt"/>.
        /// Called by <see cref="HardcodedQueryClassifier"/> with an already-parsed AST.
        /// </summary>
        public static bool TryClassify(SelectStmt selectStmt, out MetadataIntent intent)
        {
            intent = default;

            if (selectStmt == null)
                return false;

            if (TryRecognizeCharacterSets(selectStmt))
            {
                intent = MetadataIntent.CharacterSets;
                return true;
            }

            if (TryRecognizePrimaryKeyConstraints(selectStmt))
            {
                intent = MetadataIntent.PrimaryKeyConstraints;
                return true;
            }

            // Try the referential-constraints variant before the plain FK-centric classifier,
            // because it has a stronger structural anchor (subquery over referential_constraints).
            if (TryRecognizeForeignKeyRelationshipsReferential(selectStmt))
            {
                intent = MetadataIntent.ForeignKeyRelationshipsReferential;
                return true;
            }

            if (TryRecognizeForeignKeyRelationshipsFkCentric(selectStmt))
            {
                intent = MetadataIntent.ForeignKeyRelationshipsFkCentric;
                return true;
            }

            if (TryRecognizeForeignKeyRelationshipsPkCentric(selectStmt))
            {
                intent = MetadataIntent.ForeignKeyRelationshipsPkCentric;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Convenience for tests: parses <paramref name="queryText"/> as a single SELECT,
        /// classifies its PowerBI intent, and resolves it to the canonical
        /// <see cref="PgTable"/> response in one call.
        /// </summary>
        public static bool TryMatch(string queryText, out PgTable result)
        {
            result = null;

            if (AstFeatures.TryParseSingleSelect(queryText, out var selectStmt) == false)
                return false;

            if (TryClassify(selectStmt, out var intent) == false)
                return false;

            return intent.TryResolveToResponse(out result);
        }

        // ── CharacterSets ────────────────────────────────────────────────────────────────
        // Anchor: information_schema.character_sets, projecting exactly {character_set_name}.
        // WHERE clauses are tolerated.
        private static bool TryRecognizeCharacterSets(SelectStmt s)
        {
            if (AstFeatures.ReferencesTable(s, InformationSchema, "character_sets") == false)
                return false;

            return AstFeatures.ProjectedNamesEqual(s, "character_set_name");
        }

        // ── PrimaryKeyConstraints ────────────────────────────────────────────────────────
        // Anchor: table_constraints + key_column_usage, projecting at least
        // {index_name, column_name, ordinal_position, primary_key}. Extra columns tolerated.
        private static bool TryRecognizePrimaryKeyConstraints(SelectStmt s)
        {
            if (AstFeatures.ReferencesTable(s, InformationSchema, "table_constraints") == false)
                return false;

            if (AstFeatures.ReferencesTable(s, InformationSchema, "key_column_usage") == false)
                return false;

            return AstFeatures.ProjectedNamesContainAll(s,
                "index_name", "column_name", "ordinal_position", "primary_key");
        }

        // ── ForeignKeyRelationships — FK-centric shape ───────────────────────────────────
        // Distinguished from PK-centric by projecting fk_table_schema + fk_table_name
        // (the FK's own location) while pk_table_schema + pk_table_name are absent.
        private static bool TryRecognizeForeignKeyRelationshipsFkCentric(SelectStmt s)
        {
            if (HasForeignKeyRelationshipSourceTables(s) == false)
                return false;

            if (AstFeatures.ProjectedNamesContainAll(s, "fk_table_schema", "fk_table_name") == false)
                return false;

            // If PK-location names are present this is PK-centric, not FK-centric.
            if (AstFeatures.ProjectsName(s, "pk_table_schema") || AstFeatures.ProjectsName(s, "pk_table_name"))
                return false;

            if (AstFeatures.ProjectedNamesContainAll(s, "pk_column_name", "fk_column_name", "ordinal") == false)
                return false;

            return HasFkNameTarget(s);
        }

        // ── ForeignKeyRelationships — PK-centric shape ───────────────────────────────────
        // Projects pk_table_schema + pk_table_name — "what tables reference me as PK?".
        private static bool TryRecognizeForeignKeyRelationshipsPkCentric(SelectStmt s)
        {
            if (HasForeignKeyRelationshipSourceTables(s) == false)
                return false;

            if (AstFeatures.ProjectedNamesContainAll(s, "pk_table_schema", "pk_table_name") == false)
                return false;

            if (AstFeatures.ProjectedNamesContainAll(s, "pk_column_name", "fk_column_name", "ordinal") == false)
                return false;

            return HasFkNameTarget(s);
        }

        // ── ForeignKeyRelationships — referential-constraints subquery variant ───────────
        // Distinguishing structural anchor: referential_constraints is referenced inside a
        // subquery in FROM. Column names vary between PowerBI versions; the count (6) and the
        // final FK-name expression are the stable features.
        private static bool TryRecognizeForeignKeyRelationshipsReferential(SelectStmt s)
        {
            if (AstFeatures.ReferencesTable(s, InformationSchema, "key_column_usage") == false)
                return false;

            if (AstFeatures.SubqueryReferencesTable(s, InformationSchema, "referential_constraints") == false)
                return false;

            if (s.TargetList is not { Count: 6 })
                return false;

            return TryRecognizeFkNameResTarget(s.TargetList[5]?.ResTarget);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        private static bool HasForeignKeyRelationshipSourceTables(SelectStmt s)
            => AstFeatures.ReferencesTable(s, InformationSchema, "key_column_usage") &&
               AstFeatures.ReferencesTable(s, InformationSchema, "table_constraints");

        /// <summary>
        /// Returns true if any projected target looks like an FK-name column: an explicit
        /// <c>fk_name</c> alias, an unaliased <c>constraint_schema</c> column reference, or
        /// a <c>||</c> string-concatenation expression (with or without a surrounding type-cast).
        /// </summary>
        private static bool HasFkNameTarget(SelectStmt s)
        {
            if (AstFeatures.ProjectsName(s, "fk_name"))
                return true;

            if (s?.TargetList == null)
                return false;

            foreach (var t in s.TargetList)
            {
                if (TryRecognizeFkNameResTarget(t?.ResTarget))
                    return true;
            }

            return false;
        }

        private static bool TryRecognizeFkNameResTarget(ResTarget rt)
        {
            if (rt == null)
                return false;

            // Explicit alias fk_name.
            if (string.IsNullOrWhiteSpace(rt.Name) == false &&
                string.Equals(rt.Name, "fk_name", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Unaliased constraint_schema column reference.
            if (rt.Val?.ColumnRef?.Fields is { Count: > 0 } fields)
            {
                var name = fields[^1]?.String?.Sval;
                if (string.Equals(name, "constraint_schema", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // String-concatenation expression using || (possibly wrapped in a type-cast).
            // Narrowed to || specifically so arithmetic/comparison AExpr nodes do not match.
            var v = rt.Val;
            if (v == null)
                return false;

            var ae = v.AExpr;
            if (ae != null && ae.Name?.Count > 0 && ae.Name[0]?.String?.Sval == "||")
                return true;

            var castAe = v.TypeCast?.Arg?.AExpr;
            if (castAe != null && castAe.Name?.Count > 0 && castAe.Name[0]?.String?.Sval == "||")
                return true;

            return false;
        }
    }
}
