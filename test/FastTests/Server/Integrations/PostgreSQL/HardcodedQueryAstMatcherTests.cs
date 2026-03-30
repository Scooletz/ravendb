using System.Collections.Generic;
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
    }
}
