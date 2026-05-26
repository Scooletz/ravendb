using System;
using System.Collections.Generic;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Npgsql;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    internal static class NpgsqlQueryClassifier
    {
        private static readonly string[] OldFlatExpectedColumns =
        {
            "nspname", "typname", "oid", "typrelid", "typbasetype", "type", "elemoid", "ord"
        };

        public static bool TryClassify(IReadOnlyList<SelectStmt> selects, out PgTable response)
        {
            response = Classify(selects);
            return response != null;
        }

        public static bool TryMatchTypesQuery(string queryText, out PgTable result)
        {
            result = null;
            if (SelectStmtShape.TryParseSingleSelect(queryText, out var s) == false)
                return false;

            result = ClassifyTypeCatalog(s);
            return result != null;
        }

        private static PgTable Classify(IReadOnlyList<SelectStmt> selects)
        {
            if (selects == null || selects.Count != 1 || selects[0] == null)
                return null;

            return ClassifyTypeCatalog(selects[0]);
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

        // Npgsql 4.1.3–5.x+: 3 targets incl. qualified wildcard + subquery in FROM.
        private static bool IsTypeCatalogModernNested(SelectStmt s)
        {
            if (s.TargetList is not { Count: 3 })
                return false;

            if (SelectStmtShape.HasWildcardTarget(s) == false)
                return false;

            return SelectStmtShape.ContainsSubselectInFrom(s);
        }

        // Npgsql 4.1.0–4.1.2: 7 targets, projects 'typelem' (others project 'elemoid').
        private static bool IsTypeCatalogMidFlat(SelectStmt s)
        {
            if (s.TargetList is not { Count: 7 })
                return false;

            if (SelectStmtShape.ReferencesTable(s, "pg_type") == false)
                return false;

            return SelectStmtShape.ProjectsName(s, "typelem");
        }

        // Npgsql 3.2.3–3.2.7: pg_type + pg_proc in FROM, pg_class absent (4.x always joins pg_class).
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

        // Npgsql 4.0.1–4.0.12: old flat shape, array_recv branch includes the pseudo-type ('p') test.
        private static bool IsTypeCatalogOldFlatWithPseudoArrays(SelectStmt s)
        {
            if (IsOldFlatShape(s) == false)
                return false;

            return ArrayRecvBlockMentionsPseudoType(s, expected: true);
        }

        // Npgsql 4.0.0: old flat shape, array_recv branch without the pseudo-type test (added in 4.0.1).
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
