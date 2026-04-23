using System;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.PowerBI;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    /// <summary>
    /// Recognizes PowerBI's information_schema metadata probes and maps each to its
    /// canonical <see cref="PgTable"/> response. Recognizers match on source relations
    /// and projected column sets so column reordering, aliasing, and WHERE-clause
    /// variations are transparent.
    ///
    /// The foreign-key responses deserve a note: PowerBI asks about FK relationships in
    /// three syntactically distinct shapes (FK-centric, PK-centric, referential-subquery).
    /// RavenDB has no SQL foreign keys, so every FK query returns an empty result set —
    /// we distinguish the shapes only to pick the right column schema for the 0 rows.
    /// </summary>
    internal static class PowerBIQueryClassifier
    {
        private const string InformationSchema = "information_schema";

        /// <summary>
        /// Primary entry point, called by <see cref="HardcodedQueryClassifier"/> with an
        /// already-parsed single-statement SELECT.
        /// </summary>
        public static bool TryClassify(SelectStmt selectStmt, out PgTable response)
        {
            response = Classify(selectStmt);
            return response != null;
        }

        /// <summary>Convenience for tests: parses then classifies in one call.</summary>
        public static bool TryMatch(string queryText, out PgTable result)
        {
            result = null;
            if (SelectStmtShape.TryParseSingleSelect(queryText, out var selectStmt) == false)
                return false;

            result = Classify(selectStmt);
            return result != null;
        }

        // ── Dispatch ──────────────────────────────────────────────────────────────────────

        private static PgTable Classify(SelectStmt s)
        {
            if (s == null)
                return null;

            if (IsCharacterSetsQuery(s))
                return PowerBIConfig.CharacterSetsResponse;

            if (IsPrimaryKeyConstraintsQuery(s))
                return PowerBIConfig.ConstraintsResponse;

            // Try the referential-subquery shape before the plain FK-centric classifier —
            // it has a stronger structural anchor (subquery over referential_constraints).
            // Historical behavior: always FK-centric response (0 rows regardless).
            if (IsForeignKeyReferentialSubqueryQuery(s))
                return PowerBIConfig.TableSchemaResponse;

            if (IsForeignKeyFkCentricQuery(s))
                return PowerBIConfig.TableSchemaResponse;

            if (IsForeignKeyPkCentricQuery(s))
                return PowerBIConfig.TableSchemaSecondaryResponse;

            return null;
        }

        // ── CharacterSets ─────────────────────────────────────────────────────────────────
        // information_schema.character_sets, projecting exactly {character_set_name}.
        private static bool IsCharacterSetsQuery(SelectStmt s)
        {
            if (SelectStmtShape.ReferencesTable(s, InformationSchema, "character_sets") == false)
                return false;

            return SelectStmtShape.ProjectedNamesEqual(s, "character_set_name");
        }

        // ── PrimaryKeyConstraints ─────────────────────────────────────────────────────────
        // table_constraints + key_column_usage, projecting at least
        // {index_name, column_name, ordinal_position, primary_key}. Extra columns tolerated.
        private static bool IsPrimaryKeyConstraintsQuery(SelectStmt s)
        {
            if (SelectStmtShape.ReferencesTable(s, InformationSchema, "table_constraints") == false)
                return false;

            if (SelectStmtShape.ReferencesTable(s, InformationSchema, "key_column_usage") == false)
                return false;

            return SelectStmtShape.ProjectedNamesContainAll(s,
                "index_name", "column_name", "ordinal_position", "primary_key");
        }

        // ── ForeignKey: FK-centric ────────────────────────────────────────────────────────
        // Projects fk_table_schema + fk_table_name (the FK's own location); must NOT project
        // pk_table_schema/pk_table_name (that signature belongs to the PK-centric shape).
        private static bool IsForeignKeyFkCentricQuery(SelectStmt s)
        {
            if (HasForeignKeyRelationshipSourceTables(s) == false)
                return false;

            if (SelectStmtShape.ProjectedNamesContainAll(s, "fk_table_schema", "fk_table_name") == false)
                return false;

            if (SelectStmtShape.ProjectsName(s, "pk_table_schema") || SelectStmtShape.ProjectsName(s, "pk_table_name"))
                return false;

            if (SelectStmtShape.ProjectedNamesContainAll(s, "pk_column_name", "fk_column_name", "ordinal") == false)
                return false;

            return HasFkNameTarget(s);
        }

        // ── ForeignKey: PK-centric ────────────────────────────────────────────────────────
        // Projects pk_table_schema + pk_table_name — "what tables reference me as PK?".
        private static bool IsForeignKeyPkCentricQuery(SelectStmt s)
        {
            if (HasForeignKeyRelationshipSourceTables(s) == false)
                return false;

            if (SelectStmtShape.ProjectedNamesContainAll(s, "pk_table_schema", "pk_table_name") == false)
                return false;

            if (SelectStmtShape.ProjectedNamesContainAll(s, "pk_column_name", "fk_column_name", "ordinal") == false)
                return false;

            return HasFkNameTarget(s);
        }

        // ── ForeignKey: referential-subquery shape ────────────────────────────────────────
        // Distinguishing anchor: referential_constraints referenced inside a subquery in
        // FROM. Column names vary across PowerBI versions, but the target count (6) and the
        // trailing FK-name expression are stable.
        private static bool IsForeignKeyReferentialSubqueryQuery(SelectStmt s)
        {
            if (SelectStmtShape.ReferencesTable(s, InformationSchema, "key_column_usage") == false)
                return false;

            if (SelectStmtShape.SubqueryReferencesTable(s, InformationSchema, "referential_constraints") == false)
                return false;

            if (s.TargetList is not { Count: 6 })
                return false;

            return IsFkNameResTarget(s.TargetList[5]?.ResTarget);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────

        private static bool HasForeignKeyRelationshipSourceTables(SelectStmt s)
            => SelectStmtShape.ReferencesTable(s, InformationSchema, "key_column_usage")
               && SelectStmtShape.ReferencesTable(s, InformationSchema, "table_constraints");

        /// <summary>
        /// Returns true if any target looks like an FK-name column: an explicit
        /// <c>fk_name</c> alias, an unaliased <c>constraint_schema</c> reference, or a
        /// <c>||</c> string-concatenation expression (optionally wrapped in a type-cast).
        /// </summary>
        private static bool HasFkNameTarget(SelectStmt s)
        {
            if (SelectStmtShape.ProjectsName(s, "fk_name"))
                return true;

            if (s?.TargetList == null)
                return false;

            foreach (var t in s.TargetList)
            {
                if (IsFkNameResTarget(t?.ResTarget))
                    return true;
            }

            return false;
        }

        private static bool IsFkNameResTarget(ResTarget rt)
        {
            if (rt == null)
                return false;

            if (string.Equals(rt.Name, "fk_name", StringComparison.OrdinalIgnoreCase))
                return true;

            if (rt.Val?.ColumnRef?.Fields is { Count: > 0 } fields)
            {
                var name = fields[^1]?.String?.Sval;
                if (string.Equals(name, "constraint_schema", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // String-concatenation via ||, possibly wrapped in a type-cast. Narrowed to ||
            // specifically so arithmetic/comparison AExpr nodes do not match.
            var v = rt.Val;
            if (IsStringConcatExpr(v?.AExpr))
                return true;

            if (IsStringConcatExpr(v?.TypeCast?.Arg?.AExpr))
                return true;

            return false;
        }

        private static bool IsStringConcatExpr(A_Expr ae)
            => ae?.Name is { Count: > 0 } name && name[0]?.String?.Sval == "||";
    }
}
