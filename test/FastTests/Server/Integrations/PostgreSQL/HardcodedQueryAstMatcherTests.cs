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
        private const string CharacterSetsQuery =
            "select character_set_name from INFORMATION_SCHEMA.character_sets";

        private const string ConstraintsQuery =
            "select i.CONSTRAINT_SCHEMA || '_' || i.CONSTRAINT_NAME as INDEX_NAME, ii.COLUMN_NAME, ii.ORDINAL_POSITION, " +
            "case when i.CONSTRAINT_TYPE = 'PRIMARY KEY' then 'Y' else 'N' end as PRIMARY_KEY\n" +
            "from INFORMATION_SCHEMA.table_constraints i inner join INFORMATION_SCHEMA.key_column_usage ii " +
            "on i.CONSTRAINT_SCHEMA = ii.CONSTRAINT_SCHEMA and i.CONSTRAINT_NAME = ii.CONSTRAINT_NAME " +
            "and i.TABLE_SCHEMA = ii.TABLE_SCHEMA and i.TABLE_NAME = ii.TABLE_NAME";

        // Just the SELECT clause prefix; tests wrap it in a full FROM via BuildTableSchemaSql.
        private const string TableSchemaQuery =
            "select\n    pkcol.COLUMN_NAME as PK_COLUMN_NAME,\n    fkcol.TABLE_SCHEMA AS FK_TABLE_SCHEMA,\n" +
            "    fkcol.TABLE_NAME AS FK_TABLE_NAME,\n    fkcol.COLUMN_NAME as FK_COLUMN_NAME,\n" +
            "    fkcol.ORDINAL_POSITION as ORDINAL,\n    fkcon.CONSTRAINT_SCHEMA || '_' || fkcol.TABLE_NAME";

        // SELECT clause prefix for the secondary TableSchema variant.
        private const string TableSchemaSecondaryQuery =
            "select\n    pkcol.TABLE_SCHEMA AS PK_TABLE_SCHEMA,\n    pkcol.TABLE_NAME AS PK_TABLE_NAME,\n" +
            "    pkcol.COLUMN_NAME as PK_COLUMN_NAME,\n    fkcol.COLUMN_NAME as FK_COLUMN_NAME,\n" +
            "    fkcol.ORDINAL_POSITION as ORDINAL,\n    fkcon.CONSTRAINT_SCHEMA ";

        public static IEnumerable<object[]> CharacterSetsQueries()
        {
            yield return new object[] { CharacterSetsQuery };
            yield return new object[] { "SELECT character_set_name\nFROM information_schema.character_sets" };
        }

        public static IEnumerable<object[]> ConstraintsQueries()
        {
            yield return new object[] { ConstraintsQuery };
            yield return new object[]
            {
                "SELECT i.CONSTRAINT_SCHEMA || '_' || i.CONSTRAINT_NAME as INDEX_NAME, ii.COLUMN_NAME, ii.ORDINAL_POSITION, CASE WHEN i.CONSTRAINT_TYPE = 'PRIMARY KEY' THEN 'Y' ELSE 'N' END as PRIMARY_KEY FROM information_schema.table_constraints i INNER JOIN information_schema.key_column_usage ii ON i.constraint_schema = ii.constraint_schema AND i.constraint_name = ii.constraint_name AND i.table_schema = ii.table_schema AND i.table_name = ii.table_name"
            };
        }

        public static IEnumerable<object[]> TableSchemaQueries()
        {
            yield return new object[] { BuildTableSchemaSql(TableSchemaQuery) };

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
            yield return new object[] { BuildTableSchemaSecondarySql(TableSchemaSecondaryQuery + "|| '_' || fkcol.table_name") };

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

        [RavenTheory(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        [MemberData(nameof(CharacterSetsQueries))]
        public void PowerBI_CharacterSetsQuery_should_match_via_ast(string sql)
        {
            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.CharacterSetsResponse, table);
        }

        [RavenTheory(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        [MemberData(nameof(ConstraintsQueries))]
        public void PowerBI_ConstraintsQuery_should_match_via_ast(string sql)
        {
            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.ConstraintsResponse, table);
        }

        [RavenTheory(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        [MemberData(nameof(TableSchemaQueries))]
        public void PowerBI_TableSchemaQuery_should_match_via_ast(string sql)
        {
            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        [RavenTheory(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        [MemberData(nameof(TableSchemaSecondaryQueries))]
        public void PowerBI_TableSchemaSecondaryQuery_should_match_via_ast(string sql)
        {
            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaSecondaryResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_TableSchemaQuery_should_not_match_when_signature_is_wrong()
        {
            const string sql = "select pkcol.column_name as pk_column_name, fkcol.table_schema as fk_table_schema from information_schema.key_column_usage fkcol join information_schema.table_constraints fkcon on 1=1";

            Assert.False(PowerBIQueryClassifier.TryMatch(sql, out _));
        }

        // ---- CharacterSets: WHERE tolerance + wrong-table rejection ----

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_CharacterSetsQuery_with_where_clause_should_still_match()
        {
            const string sql = "SELECT character_set_name FROM information_schema.character_sets WHERE character_set_name = 'UTF8'";

            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.CharacterSetsResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_CharacterSetsQuery_targeting_wrong_table_should_not_match()
        {
            const string sql = "SELECT character_set_name FROM information_schema.tables";

            Assert.False(PowerBIQueryClassifier.TryMatch(sql, out _));
        }

        // ---- Constraints: ORDER BY tolerance + wrong-table rejection ----

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_ConstraintsQuery_with_order_by_should_still_match()
        {
            // ConstraintsQuery with an ORDER BY appended — source tables and projected columns are what matter.
            var sql = ConstraintsQuery + "\norder by index_name";

            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.ConstraintsResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_ConstraintsQuery_missing_required_table_should_not_match()
        {
            // Only key_column_usage present — table_constraints is missing.
            const string sql = "SELECT ii.CONSTRAINT_SCHEMA || '_' || ii.CONSTRAINT_NAME as INDEX_NAME, ii.COLUMN_NAME, ii.ORDINAL_POSITION, 'N' as PRIMARY_KEY FROM information_schema.key_column_usage ii";

            Assert.False(PowerBIQueryClassifier.TryMatch(sql, out _));
        }

        // ---- TableSchema: wrong-table rejection ----

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_TableSchemaQuery_targeting_wrong_tables_should_not_match()
        {
            // Uses information_schema.columns instead of the required key_column_usage + table_constraints.
            const string sql = "select col.column_name as pk_column_name, col.table_schema as fk_table_schema, col.table_name as fk_table_name, col.column_name as fk_column_name, col.ordinal_position as ordinal, 'x' as fk_name from information_schema.columns col";

            Assert.False(PowerBIQueryClassifier.TryMatch(sql, out _));
        }

        // ---- TableSchemaSecondary: wrong-table rejection ----

        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_TableSchemaSecondaryQuery_targeting_wrong_tables_should_not_match()
        {
            const string sql = "select col.table_schema as pk_table_schema, col.table_name as pk_table_name, col.column_name as pk_column_name, col.column_name as fk_column_name, col.ordinal_position as ordinal, col.table_schema from information_schema.columns col";

            Assert.False(PowerBIQueryClassifier.TryMatch(sql, out _));
        }

        // ---- ReferentialConstraintsFk ----

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

        // ---- Intent-based tolerance: projection reordering ----

        // Old position-strict matcher required pk_column_name at index 0, fk_table_schema at index 1, etc.
        // The intent recognizer looks at the set of projected names, so reordering must still match.
        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_TableSchemaQuery_reordered_projection_should_still_match()
        {
            const string sql =
                "select " +
                "fkcol.ordinal_position as ordinal, " +
                "fkcol.column_name as fk_column_name, " +
                "fkcol.table_name as fk_table_name, " +
                "fkcol.table_schema as fk_table_schema, " +
                "pkcol.column_name as pk_column_name, " +
                "fkcon.constraint_schema || '_' || fkcol.table_name as fk_name\n" +
                "from information_schema.key_column_usage pkcol\n" +
                "join information_schema.key_column_usage fkcol on 1=1\n" +
                "join information_schema.table_constraints fkcon on 1=1";

            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaResponse, table);
        }

        // Same story for the PK-centric variant.
        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_TableSchemaSecondaryQuery_reordered_projection_should_still_match()
        {
            const string sql =
                "select " +
                "fkcol.ordinal_position as ordinal, " +
                "fkcol.column_name as fk_column_name, " +
                "pkcol.column_name as pk_column_name, " +
                "pkcol.table_name as pk_table_name, " +
                "pkcol.table_schema as pk_table_schema, " +
                "fkcon.constraint_schema || '_' || fkcol.table_name as fk_name\n" +
                "from information_schema.key_column_usage pkcol\n" +
                "join information_schema.key_column_usage fkcol on 1=1\n" +
                "join information_schema.table_constraints fkcon on 1=1";

            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.TableSchemaSecondaryResponse, table);
        }

        // The ConstraintsQuery intent tolerates extra trailing columns (intent signature =
        // required-columns-subset), so adding a harmless column must not break recognition.
        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_ConstraintsQuery_extra_projected_column_should_still_match()
        {
            const string sql =
                "select i.CONSTRAINT_SCHEMA || '_' || i.CONSTRAINT_NAME as INDEX_NAME, ii.COLUMN_NAME, " +
                "ii.ORDINAL_POSITION, 'N' as PRIMARY_KEY, ii.CONSTRAINT_NAME as constraint_name " +
                "from information_schema.table_constraints i " +
                "inner join information_schema.key_column_usage ii on i.CONSTRAINT_NAME = ii.CONSTRAINT_NAME";

            Assert.True(PowerBIQueryClassifier.TryMatch(sql, out var table));
            Assert.Same(PowerBIConfig.ConstraintsResponse, table);
        }

        // Structural guard: a query that just has the right *table* pair but no overlap with the
        // required FK-shape column set must still be rejected.
        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_TableSchemaQuery_right_tables_but_unrelated_projection_should_not_match()
        {
            const string sql =
                "select kc.table_catalog, kc.constraint_catalog " +
                "from information_schema.key_column_usage kc " +
                "join information_schema.table_constraints tc on kc.constraint_name = tc.constraint_name";

            Assert.False(PowerBIQueryClassifier.TryMatch(sql, out _));
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

        // Same source tables as FkCentric (key_column_usage + table_constraints) but the
        // projected column set is only the PK subset — arithmetic in the 6th slot must NOT
        // satisfy the FK-name anchor (guards the AExpr || narrowing).
        [RavenFact(RavenTestCategory.PostgreSql | RavenTestCategory.PowerBi)]
        public void PowerBI_FkCentric_arithmetic_expression_in_sixth_slot_should_not_match()
        {
            const string sql =
                "select pkcol.column_name as pk_column_name, fkcol.table_schema as fk_table_schema, " +
                "fkcol.table_name as fk_table_name, fkcol.column_name as fk_column_name, " +
                "fkcol.ordinal_position as ordinal, fkcol.ordinal_position * 2 " +
                "from information_schema.key_column_usage pkcol " +
                "join information_schema.key_column_usage fkcol on 1=1 " +
                "join information_schema.table_constraints fkcon on 1=1";

            Assert.False(PowerBIQueryClassifier.TryMatch(sql, out _));
        }

    }
}
