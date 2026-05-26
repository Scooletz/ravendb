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

            // Only the referential-subquery shape stays classifier-handled — it uses a FROM-subquery
            // (information_schema.referential_constraints), which the virtual-tables interpreter
            // doesn't support yet. PrimaryKeyConstraints / FK FkCentric / FK PkCentric are all
            // empty-result inner joins now routed through PgVirtualInterpreter.
            if (IsForeignKeyReferentialSubqueryQuery(s))
                return PowerBIConfig.TableSchemaResponse;

            return null;
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
