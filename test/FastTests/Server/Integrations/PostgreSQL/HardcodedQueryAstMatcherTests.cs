using System;
using System.Collections.Generic;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.PowerBI;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    public sealed class HardcodedQueryAstMatcherTests
    {
        public static IEnumerable<object[]> CharacterSetsQueries()
        {
            yield return new object[] { PowerBIConfig.CharacterSetsQuery };
            yield return new object[] { "SELECT character_set_name\nFROM information_schema.character_sets" };
        }

        public static IEnumerable<object[]> ConstraintsQueries()
        {
            yield return new object[] { PowerBIConfig.ConstraintsQuery };
            yield return new object[]
            {
                "SELECT i.CONSTRAINT_SCHEMA || '_' || i.CONSTRAINT_NAME as INDEX_NAME, ii.COLUMN_NAME, ii.ORDINAL_POSITION, CASE WHEN i.CONSTRAINT_TYPE = 'PRIMARY KEY' THEN 'Y' ELSE 'N' END as PRIMARY_KEY FROM information_schema.table_constraints i INNER JOIN information_schema.key_column_usage ii ON i.constraint_schema = ii.constraint_schema AND i.constraint_name = ii.constraint_name AND i.table_schema = ii.table_schema AND i.table_name = ii.table_name"
            };
        }

        public static IEnumerable<object[]> TableSchemaQueries()
        {
            yield return new object[] { BuildTableSchemaSql(PowerBIConfig.TableSchemaQuery) };

            yield return new object[]
            {
                BuildTableSchemaSql(
                    "select " +
                    "pkcol.column_name as pk_column_name, " +
                    "fkcol.table_schema as fk_table_schema, " +
                    "fkcol.table_name as fk_table_name, " +
                    "fkcol.column_name as fk_column_name, " +
                    "fkcol.ordinal_position as ordinal, " +
                    "fkcon.constraint_schema || '_' || fkcol.table_name")
            };
        }

        public static IEnumerable<object[]> TableSchemaSecondaryQueries()
        {
            yield return new object[] { BuildTableSchemaSecondarySql(PowerBIConfig.TableSchemaSecondaryQuery + "|| '_' || fkcol.table_name") };

            yield return new object[]
            {
                BuildTableSchemaSecondarySql(
                    "select " +
                    "pkcol.table_schema as pk_table_schema, " +
                    "pkcol.table_name as pk_table_name, " +
                    "pkcol.column_name as pk_column_name, " +
                    "fkcol.column_name as fk_column_name, " +
                    "fkcol.ordinal_position as ordinal, " +
                    "fkcon.constraint_schema")
            };
        }

        private static string BuildTableSchemaSql(string selectClause) => $@"
{selectClause}
from information_schema.key_column_usage pkcol
join information_schema.key_column_usage fkcol on 1=1
join information_schema.table_constraints fkcon on 1=1";

        private static string BuildTableSchemaSecondarySql(string selectClause) => $@"
{selectClause}
from information_schema.key_column_usage pkcol
join information_schema.key_column_usage fkcol on 1=1
join information_schema.table_constraints fkcon on 1=1";

        [Theory]
        [MemberData(nameof(CharacterSetsQueries))]
        public void PowerBI_CharacterSetsQuery_should_match_via_ast(string sql)
        {
            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            Assert.Same(PowerBIConfig.CharacterSetsResponse, table);
        }

        [Theory]
        [MemberData(nameof(ConstraintsQueries))]
        public void PowerBI_ConstraintsQuery_should_match_via_ast(string sql)
        {
            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            Assert.Same(PowerBIConfig.ConstraintsResponse, table);
        }

        [Theory]
        [MemberData(nameof(TableSchemaQueries))]
        public void PowerBI_TableSchemaQuery_should_match_via_ast(string sql)
        {
            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        [Theory]
        [MemberData(nameof(TableSchemaSecondaryQueries))]
        public void PowerBI_TableSchemaSecondaryQuery_should_match_via_ast(string sql)
        {
            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaSecondaryResponse, table);
        }

        [Fact]
        public void PowerBI_TableSchemaQuery_should_not_match_when_signature_is_wrong()
        {
            const string sql = "select pkcol.column_name as pk_column_name, fkcol.table_schema as fk_table_schema from information_schema.key_column_usage fkcol join information_schema.table_constraints fkcon on 1=1";

            Assert.False(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out _));
        }

        // ---- CharacterSets: WHERE tolerance + wrong-table rejection ----

        [Fact]
        public void PowerBI_CharacterSetsQuery_with_where_clause_should_still_match()
        {
            const string sql = "SELECT character_set_name FROM information_schema.character_sets WHERE character_set_name = 'UTF8'";

            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            Assert.Same(PowerBIConfig.CharacterSetsResponse, table);
        }

        [Fact]
        public void PowerBI_CharacterSetsQuery_targeting_wrong_table_should_not_match()
        {
            const string sql = "SELECT character_set_name FROM information_schema.tables";

            Assert.False(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out _));
        }

        // ---- Constraints: ORDER BY tolerance + wrong-table rejection ----

        [Fact]
        public void PowerBI_ConstraintsQuery_with_order_by_should_still_match()
        {
            // ConstraintsQuery with an ORDER BY appended — source tables and projected columns are what matter.
            var sql = PowerBIConfig.ConstraintsQuery + "\norder by index_name";

            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            Assert.Same(PowerBIConfig.ConstraintsResponse, table);
        }

        [Fact]
        public void PowerBI_ConstraintsQuery_missing_required_table_should_not_match()
        {
            // Only key_column_usage present — table_constraints is missing.
            const string sql = "SELECT ii.CONSTRAINT_SCHEMA || '_' || ii.CONSTRAINT_NAME as INDEX_NAME, ii.COLUMN_NAME, ii.ORDINAL_POSITION, 'N' as PRIMARY_KEY FROM information_schema.key_column_usage ii";

            Assert.False(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out _));
        }

        // ---- TableSchema: wrong-table rejection ----

        [Fact]
        public void PowerBI_TableSchemaQuery_targeting_wrong_tables_should_not_match()
        {
            // Uses information_schema.columns instead of the required key_column_usage + table_constraints.
            const string sql = "select col.column_name as pk_column_name, col.table_schema as fk_table_schema, col.table_name as fk_table_name, col.column_name as fk_column_name, col.ordinal_position as ordinal, 'x' as fk_name from information_schema.columns col";

            Assert.False(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out _));
        }

        // ---- TableSchemaSecondary: wrong-table rejection ----

        [Fact]
        public void PowerBI_TableSchemaSecondaryQuery_targeting_wrong_tables_should_not_match()
        {
            const string sql = "select col.table_schema as pk_table_schema, col.table_name as pk_table_name, col.column_name as pk_column_name, col.column_name as fk_column_name, col.ordinal_position as ordinal, col.table_schema from information_schema.columns col";

            Assert.False(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out _));
        }

        // ---- ReferentialConstraintsFk: exact failing query + variants + negative cases ----

        [Fact]
        public void PowerBI_ReferentialConstraintsFkQuery_exact_failing_query_should_match()
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

            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            // Empty rows — RavenDB has no SQL foreign keys.
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        // Same shape, different collection name in WHERE/FK_NAME — must still match.
        [Fact]
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

            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        [Fact]
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

            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        // Negative: right structure but 5 projected columns instead of 6.
        [Fact]
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

            Assert.False(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out _));
        }

        // Negative: subquery targets information_schema.columns instead of referential_constraints.
        [Fact]
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

            Assert.False(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out _));
        }

        // ---- ReferentialConstraintsFk: secondary variant (PK_TABLE_SCHEMA/PK_TABLE_NAME projection) ----

        [Fact]
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

            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            // Empty rows — RavenDB has no SQL foreign keys.
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        // Regression guard: original primary variant must still match after the secondary variant was added.
        [Fact]
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

            Assert.True(PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        [Fact]
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

    }
}
