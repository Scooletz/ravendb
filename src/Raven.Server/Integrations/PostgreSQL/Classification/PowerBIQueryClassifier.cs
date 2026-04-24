using System;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.PowerBI;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    internal static class PowerBIQueryClassifier
    {
        private const string InformationSchema = "information_schema";

        public static bool TryClassify(SelectStmt selectStmt, out PgTable response)
        {
            response = Classify(selectStmt);
            return response != null;
        }

        public static bool TryMatch(string queryText, out PgTable result)
        {
            result = null;
            if (SelectStmtShape.TryParseSingleSelect(queryText, out var selectStmt) == false)
                return false;

            result = Classify(selectStmt);
            return result != null;
        }

        private static PgTable Classify(SelectStmt s)
        {
            if (s == null)
                return null;

            if (IsCharacterSetsQuery(s))
                return PowerBIConfig.CharacterSetsResponse;

            if (IsPrimaryKeyConstraintsQuery(s))
                return PowerBIConfig.ConstraintsResponse;

            // Referential-subquery shape goes before plain FK-centric — stronger structural anchor.
            if (IsForeignKeyReferentialSubqueryQuery(s))
                return PowerBIConfig.TableSchemaResponse;

            if (IsForeignKeyFkCentricQuery(s))
                return PowerBIConfig.TableSchemaResponse;

            if (IsForeignKeyPkCentricQuery(s))
                return PowerBIConfig.TableSchemaSecondaryResponse;

            return null;
        }

        private static bool IsCharacterSetsQuery(SelectStmt s)
        {
            if (SelectStmtShape.ReferencesTable(s, InformationSchema, "character_sets") == false)
                return false;

            return SelectStmtShape.ProjectedNamesEqual(s, "character_set_name");
        }

        private static bool IsPrimaryKeyConstraintsQuery(SelectStmt s)
        {
            if (SelectStmtShape.ReferencesTable(s, InformationSchema, "table_constraints") == false)
                return false;

            if (SelectStmtShape.ReferencesTable(s, InformationSchema, "key_column_usage") == false)
                return false;

            return SelectStmtShape.ProjectedNamesContainAll(s,
                "index_name", "column_name", "ordinal_position", "primary_key");
        }

        // Projects fk_table_schema + fk_table_name; must NOT project pk_table_schema/pk_table_name.
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

        // Anchor: referential_constraints inside a FROM-subquery. Column names vary; target count (6) is stable.
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

        private static bool HasForeignKeyRelationshipSourceTables(SelectStmt s)
            => SelectStmtShape.ReferencesTable(s, InformationSchema, "key_column_usage")
               && SelectStmtShape.ReferencesTable(s, InformationSchema, "table_constraints");

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

            // Narrowed to || so arithmetic/comparison AExpr nodes do not match.
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
