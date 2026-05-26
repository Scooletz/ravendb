using System;
using System.Collections.Generic;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.Classification;
using Raven.Server.Integrations.PostgreSQL.PowerBI;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    public sealed class PowerBIQueryClassifierTests(ITestOutputHelper output) : NoDisposalNeeded(output)
    {
        // Canonical SQL strings sent by PowerBI Desktop — used as recognition test inputs.
        // The recognizer is structurally tolerant of equivalent rewrites (different casing,
        // added ORDER BY, extra WHERE clauses, reordered projections, etc.).

        // ---- ReferentialConstraintsFk ----
        // The classifier only handles the FROM-subquery shape (referential_constraints) now.
        // Plain INNER JOIN over key_column_usage / table_constraints (PrimaryKeyConstraints,
        // FK FkCentric, FK PkCentric) is routed through PgVirtualInterpreter via the empty-join
        // shortcut over information_schema virtual tables.

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_ReferentialConstraintsFkQuery_query_should_match()
        {
            const string sql =
                "select pkcol.COLUMN_NAME as PK_COLUMN_NAME, fkcol.TABLE_SCHEMA AS FK_TABLE_SCHEMA, " +
                "fkcol.TABLE_NAME AS FK_TABLE_NAME, fkcol.COLUMN_NAME as FK_COLUMN_NAME, " +
                "fkcol.ORDINAL_POSITION as ORDINAL, " +
                "fkcon.CONSTRAINT_SCHEMA || '*' || fkcol.TABLE_NAME || '*' || 'Employees' || '_' || fkcon.CONSTRAINT_NAME as FK_NAME\n" +
                "from\n" +
                "(select distinct constraint_catalog, constraint_schema, unique_constraint_schema, constraint_name, unique_constraint_name " +
                "from INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS) fkcon\n" +
                "inner join INFORMATION_SCHEMA.KEY_COLUMN_USAGE fkcol on fkcon.CONSTRAINT_SCHEMA = fkcol.CONSTRAINT_SCHEMA and fkcon.CONSTRAINT_NAME = fkcol.CONSTRAINT_NAME\n" +
                "inner join INFORMATION_SCHEMA.KEY_COLUMN_USAGE pkcol on fkcon.UNIQUE_CONSTRAINT_SCHEMA = pkcol.CONSTRAINT_SCHEMA and fkcon.UNIQUE_CONSTRAINT_NAME = pkcol.CONSTRAINT_NAME\n" +
                "where pkcol.TABLE_SCHEMA = 'public' and pkcol.TABLE_NAME = 'Employees' and pkcol.ORDINAL_POSITION = fkcol.ORDINAL_POSITION\n" +
                "order by FK_NAME, fkcol.ORDINAL_POSITION";

            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            // Empty rows — RavenDB has no SQL foreign keys.
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        // Same shape, different collection name in WHERE/FK_NAME — must still match.
        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_ReferentialConstraintsFkQuery_different_collection_should_match()
        {
            const string sql =
                "select pkcol.COLUMN_NAME as PK_COLUMN_NAME, fkcol.TABLE_SCHEMA AS FK_TABLE_SCHEMA, " +
                "fkcol.TABLE_NAME AS FK_TABLE_NAME, fkcol.COLUMN_NAME as FK_COLUMN_NAME, " +
                "fkcol.ORDINAL_POSITION as ORDINAL, " +
                "fkcon.CONSTRAINT_SCHEMA || '*' || fkcol.TABLE_NAME || '*' || 'Products' || '_' || fkcon.CONSTRAINT_NAME as FK_NAME\n" +
                "from\n" +
                "(select distinct constraint_catalog, constraint_schema, unique_constraint_schema, constraint_name, unique_constraint_name " +
                "from INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS) fkcon\n" +
                "inner join INFORMATION_SCHEMA.KEY_COLUMN_USAGE fkcol on fkcon.CONSTRAINT_SCHEMA = fkcol.CONSTRAINT_SCHEMA and fkcon.CONSTRAINT_NAME = fkcol.CONSTRAINT_NAME\n" +
                "inner join INFORMATION_SCHEMA.KEY_COLUMN_USAGE pkcol on fkcon.UNIQUE_CONSTRAINT_SCHEMA = pkcol.CONSTRAINT_SCHEMA and fkcon.UNIQUE_CONSTRAINT_NAME = pkcol.CONSTRAINT_NAME\n" +
                "where pkcol.TABLE_SCHEMA = 'public' and pkcol.TABLE_NAME = 'Products' and pkcol.ORDINAL_POSITION = fkcol.ORDINAL_POSITION\n" +
                "order by FK_NAME, fkcol.ORDINAL_POSITION";

            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_ReferentialConstraintsFkQuery_lowercase_identifiers_should_match()
        {
            const string sql =
                "select pkcol.column_name as pk_column_name, fkcol.table_schema as fk_table_schema, " +
                "fkcol.table_name as fk_table_name, fkcol.column_name as fk_column_name, " +
                "fkcol.ordinal_position as ordinal, " +
                "fkcon.constraint_schema || '_' || fkcol.table_name as fk_name\n" +
                "from\n" +
                "(select distinct constraint_schema, unique_constraint_schema, constraint_name, unique_constraint_name " +
                "from information_schema.referential_constraints) fkcon\n" +
                "inner join information_schema.key_column_usage fkcol on fkcon.constraint_schema = fkcol.constraint_schema\n" +
                "inner join information_schema.key_column_usage pkcol on fkcon.unique_constraint_schema = pkcol.constraint_schema\n" +
                "where pkcol.table_name = 'Orders'";

            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        // Negative: right structure but 5 projected columns instead of 6.
        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_ReferentialConstraintsFkQuery_wrong_column_count_should_not_match()
        {
            // Only 5 projected columns instead of 6 — missing the fk_name expression.
            const string sql =
                "select pkcol.column_name as pk_column_name, fkcol.table_schema as fk_table_schema, " +
                "fkcol.table_name as fk_table_name, fkcol.column_name as fk_column_name, " +
                "fkcol.ordinal_position as ordinal\n" +
                "from\n" +
                "(select distinct constraint_schema from information_schema.referential_constraints) fkcon\n" +
                "inner join information_schema.key_column_usage fkcol on fkcon.constraint_schema = fkcol.constraint_schema\n" +
                "inner join information_schema.key_column_usage pkcol on 1=1";

            Assert.False(PowerBIQueryClassifier.TryMatch(sql, out _));
        }

        // Negative: subquery targets information_schema.columns instead of referential_constraints.
        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_ReferentialConstraintsFkQuery_wrong_subquery_source_should_not_match()
        {
            // Uses information_schema.columns (not referential_constraints) in the subquery,
            // and no table_constraints either → no existing matcher fires.
            const string sql =
                "select pkcol.column_name as pk_column_name, fkcol.table_schema as fk_table_schema, " +
                "fkcol.table_name as fk_table_name, fkcol.column_name as fk_column_name, " +
                "fkcol.ordinal_position as ordinal, fkcon.table_name as fk_name\n" +
                "from\n" +
                "(select distinct table_schema, table_name from information_schema.columns) fkcon\n" +
                "inner join information_schema.key_column_usage fkcol on 1=1\n" +
                "inner join information_schema.key_column_usage pkcol on 1=1";

            Assert.False(PowerBIQueryClassifier.TryMatch(sql, out _));
        }

        // ---- ReferentialConstraintsFk: secondary variant (PK_TABLE_SCHEMA/PK_TABLE_NAME projection) ----

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_ReferentialConstraintsFkQuery_secondary_variant_exact_failing_query_should_match()
        {
            const string sql =
                "select pkcol.TABLE_SCHEMA AS PK_TABLE_SCHEMA, pkcol.TABLE_NAME AS PK_TABLE_NAME, " +
                "pkcol.COLUMN_NAME as PK_COLUMN_NAME, fkcol.COLUMN_NAME as FK_COLUMN_NAME, " +
                "fkcol.ORDINAL_POSITION as ORDINAL, " +
                "fkcon.CONSTRAINT_SCHEMA || '*' || 'Employees' || '*' || pkcol.TABLE_NAME || '_' || fkcon.CONSTRAINT_NAME as FK_NAME\n" +
                "from\n" +
                "(select distinct constraint_catalog, constraint_schema, unique_constraint_schema, constraint_name, unique_constraint_name " +
                "from INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS) fkcon\n" +
                "inner join INFORMATION_SCHEMA.KEY_COLUMN_USAGE fkcol on fkcon.CONSTRAINT_SCHEMA = fkcol.CONSTRAINT_SCHEMA and fkcon.CONSTRAINT_NAME = fkcol.CONSTRAINT_NAME\n" +
                "inner join INFORMATION_SCHEMA.KEY_COLUMN_USAGE pkcol on fkcon.UNIQUE_CONSTRAINT_SCHEMA = pkcol.CONSTRAINT_SCHEMA and fkcon.UNIQUE_CONSTRAINT_NAME = pkcol.CONSTRAINT_NAME\n" +
                "where fkcol.TABLE_SCHEMA = 'public' and fkcol.TABLE_NAME = 'Employees' and pkcol.ORDINAL_POSITION = fkcol.ORDINAL_POSITION\n" +
                "order by FK_NAME, fkcol.ORDINAL_POSITION";

            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            // Empty rows — RavenDB has no SQL foreign keys.
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        // Regression guard: original primary variant must still match after the secondary variant was added.
        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_ReferentialConstraintsFkQuery_primary_variant_still_matches_after_broadening()
        {
            // This is the query handled in the previous pass — re-verified here as a regression guard.
            const string sql =
                "select pkcol.COLUMN_NAME as PK_COLUMN_NAME, fkcol.TABLE_SCHEMA AS FK_TABLE_SCHEMA, " +
                "fkcol.TABLE_NAME AS FK_TABLE_NAME, fkcol.COLUMN_NAME as FK_COLUMN_NAME, " +
                "fkcol.ORDINAL_POSITION as ORDINAL, " +
                "fkcon.CONSTRAINT_SCHEMA || '*' || fkcol.TABLE_NAME || '*' || 'Orders' || '_' || fkcon.CONSTRAINT_NAME as FK_NAME\n" +
                "from\n" +
                "(select distinct constraint_catalog, constraint_schema, unique_constraint_schema, constraint_name, unique_constraint_name " +
                "from INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS) fkcon\n" +
                "inner join INFORMATION_SCHEMA.KEY_COLUMN_USAGE fkcol on fkcon.CONSTRAINT_SCHEMA = fkcol.CONSTRAINT_SCHEMA and fkcon.CONSTRAINT_NAME = fkcol.CONSTRAINT_NAME\n" +
                "inner join INFORMATION_SCHEMA.KEY_COLUMN_USAGE pkcol on fkcon.UNIQUE_CONSTRAINT_SCHEMA = pkcol.CONSTRAINT_SCHEMA and fkcon.UNIQUE_CONSTRAINT_NAME = pkcol.CONSTRAINT_NAME\n" +
                "where pkcol.TABLE_SCHEMA = 'public' and pkcol.TABLE_NAME = 'Orders' and pkcol.ORDINAL_POSITION = fkcol.ORDINAL_POSITION\n" +
                "order by FK_NAME, fkcol.ORDINAL_POSITION";

            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void HardcodedQuery_ReferentialConstraintsFkVariant2_is_claimed_at_dispatch_level()
        {
            const string sql =
                "select pkcol.TABLE_SCHEMA AS PK_TABLE_SCHEMA, pkcol.TABLE_NAME AS PK_TABLE_NAME, " +
                "pkcol.COLUMN_NAME as PK_COLUMN_NAME, fkcol.COLUMN_NAME as FK_COLUMN_NAME, " +
                "fkcol.ORDINAL_POSITION as ORDINAL, " +
                "fkcon.CONSTRAINT_SCHEMA || '*' || 'Employees' || '*' || pkcol.TABLE_NAME || '_' || fkcon.CONSTRAINT_NAME as FK_NAME\n" +
                "from\n" +
                "(select distinct constraint_catalog, constraint_schema, unique_constraint_schema, constraint_name, unique_constraint_name " +
                "from INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS) fkcon\n" +
                "inner join INFORMATION_SCHEMA.KEY_COLUMN_USAGE fkcol on fkcon.CONSTRAINT_SCHEMA = fkcol.CONSTRAINT_SCHEMA and fkcon.CONSTRAINT_NAME = fkcol.CONSTRAINT_NAME\n" +
                "inner join INFORMATION_SCHEMA.KEY_COLUMN_USAGE pkcol on fkcon.UNIQUE_CONSTRAINT_SCHEMA = pkcol.CONSTRAINT_SCHEMA and fkcon.UNIQUE_CONSTRAINT_NAME = pkcol.CONSTRAINT_NAME\n" +
                "where fkcol.TABLE_SCHEMA = 'public' and fkcol.TABLE_NAME = 'Employees' and pkcol.ORDINAL_POSITION = fkcol.ORDINAL_POSITION\n" +
                "order by FK_NAME, fkcol.ORDINAL_POSITION";

            Assert.True(HardcodedQuery.TryParse(sql, Array.Empty<int>(), session: null, out var hardcodedQuery));
            Assert.NotNull(hardcodedQuery);
        }

        // ---- Cross-source guard: PowerBI recognizer must not claim Npgsql queries ----

        // A representative Npgsql pg_catalog metadata query (EnumTypeLabels) targets pg_enum
        // and pg_type — completely different source tables from any PowerBI intent.
        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_recognizer_does_not_claim_Npgsql_enum_type_labels_query()
        {
            const string sql =
                "SELECT pg_type.oid, enumlabel FROM pg_enum JOIN pg_type ON pg_type.oid = enumtypid";

            Assert.False(PowerBIQueryClassifier.TryMatch(sql, out _));
        }
    }
}
