using System.Text;
using Raven.Server.Integrations.PostgreSQL.VirtualCatalog;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL.VirtualCatalog
{
    public sealed class PgVirtualInterpreterTests(ITestOutputHelper output) : NoDisposalNeeded(output)
    {
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Version_function_returns_single_row()
        {
            Assert.True(PgVirtualInterpreter.TryExecute("select version()", EmptyCtx(), out var table));
            Assert.NotNull(table);
            Assert.Single(table.Columns);
            Assert.Equal("version", table.Columns[0].Name);
            Assert.Single(table.Data);
            Assert.StartsWith("PostgreSQL", DecodeCell(table, row: 0, column: 0));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Current_setting_max_index_keys_returns_32()
        {
            Assert.True(PgVirtualInterpreter.TryExecute("select current_setting('max_index_keys')", EmptyCtx(), out var table));
            Assert.NotNull(table);
            Assert.Single(table.Data);
            Assert.Equal("32", DecodeCell(table, row: 0, column: 0));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Current_setting_unknown_setting_falls_through()
        {
            Assert.False(PgVirtualInterpreter.TryExecute("select current_setting('not_a_real_setting')", EmptyCtx(), out var table));
            Assert.Null(table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Version_function_with_alias_uses_alias_as_column_name()
        {
            Assert.True(PgVirtualInterpreter.TryExecute("select version() as v", EmptyCtx(), out var table));
            Assert.NotNull(table);
            Assert.Single(table.Columns);
            Assert.Equal("v", table.Columns[0].Name);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Star_projection_over_character_sets_returns_full_table()
        {
            Assert.True(PgVirtualInterpreter.TryExecute("select * from information_schema.character_sets", EmptyCtx(), out var table));
            Assert.Single(table.Columns);
            Assert.Equal("character_set_name", table.Columns[0].Name);
            Assert.Single(table.Data);
            Assert.Equal("UTF8", DecodeCell(table, row: 0, column: 0));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Named_projection_over_character_sets_returns_single_column()
        {
            Assert.True(PgVirtualInterpreter.TryExecute("select character_set_name from information_schema.character_sets", EmptyCtx(), out var table));
            Assert.Single(table.Columns);
            Assert.Single(table.Data);
            Assert.Equal("UTF8", DecodeCell(table, row: 0, column: 0));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Schema_and_table_lookup_is_case_insensitive()
        {
            Assert.True(PgVirtualInterpreter.TryExecute("SELECT character_set_name FROM INFORMATION_SCHEMA.CHARACTER_SETS", EmptyCtx(), out var table));
            Assert.Single(table.Data);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Where_equals_matching_value_returns_one_row()
        {
            Assert.True(PgVirtualInterpreter.TryExecute(
                "select character_set_name from information_schema.character_sets where character_set_name = 'UTF8'",
                EmptyCtx(), out var table));
            Assert.Single(table.Data);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Where_in_with_matching_value_returns_one_row()
        {
            Assert.True(PgVirtualInterpreter.TryExecute(
                "select character_set_name from information_schema.character_sets where character_set_name in ('UTF8','LATIN1')",
                EmptyCtx(), out var table));
            Assert.Single(table.Data);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Limit_zero_returns_empty_data()
        {
            Assert.True(PgVirtualInterpreter.TryExecute(
                "select character_set_name from information_schema.character_sets limit 0",
                EmptyCtx(), out var table));
            Assert.Empty(table.Data);
        }
        
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Unknown_table_is_rejected()
        {
            Assert.False(PgVirtualInterpreter.TryExecute("select * from no_such.foo", EmptyCtx(), out var table));
            Assert.Null(table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Group_by_is_rejected()
        {
            Assert.False(PgVirtualInterpreter.TryExecute(
                "select character_set_name from information_schema.character_sets group by character_set_name",
                EmptyCtx(), out var table));
            Assert.Null(table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Join_is_rejected()
        {
            Assert.False(PgVirtualInterpreter.TryExecute(
                "select * from information_schema.character_sets a join information_schema.character_sets b on 1=1",
                EmptyCtx(), out var table));
            Assert.Null(table);
        }
        
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Information_schema_tables_with_null_db_returns_empty_rowset()
        {
            Assert.True(PgVirtualInterpreter.TryExecute(
                "select table_schema from information_schema.tables",
                EmptyCtx(), out var table));
            Assert.Single(table.Columns);
            Assert.Empty(table.Data);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Information_schema_tables_with_where_and_null_db_returns_empty_rowset()
        {
            Assert.True(PgVirtualInterpreter.TryExecute(
                "select table_schema, table_name, table_type from information_schema.tables where table_type = 'BASE TABLE'",
                EmptyCtx(), out var table));
            Assert.Equal(3, table.Columns.Count);
            Assert.Empty(table.Data);
        }

        // ── Multi-statement batches (Npgsql startup pair) ────────────────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Paired_version_and_current_setting_probe_merges_columns()
        {
            Assert.True(PgVirtualInterpreter.TryExecute(
                "select version();select current_setting('max_index_keys')",
                EmptyCtx(), out var table));

            Assert.Equal(2, table.Columns.Count);
            Assert.Equal("version", table.Columns[0].Name);
            Assert.Equal("current_setting", table.Columns[1].Name);
            Assert.Single(table.Data);
            Assert.StartsWith("PostgreSQL", DecodeCell(table, row: 0, column: 0));
            Assert.Equal("32", DecodeCell(table, row: 0, column: 1));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Multi_statement_with_unsupported_statement_falls_through()
        {
            // First statement is fine; second targets an unknown table → reject the batch.
            Assert.False(PgVirtualInterpreter.TryExecute(
                "select version(); select * from no_such.foo",
                EmptyCtx(), out var table));
            Assert.Null(table);
        }

        // ── PowerBI PK / FK metadata empty-join shapes ────────────────────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void PrimaryKeyConstraints_query_returns_empty_rowset_with_4_columns()
        {
            const string sql =
                "select i.CONSTRAINT_SCHEMA || '_' || i.CONSTRAINT_NAME as INDEX_NAME, ii.COLUMN_NAME, ii.ORDINAL_POSITION, " +
                "case when i.CONSTRAINT_TYPE = 'PRIMARY KEY' then 'Y' else 'N' end as PRIMARY_KEY\n" +
                "from INFORMATION_SCHEMA.table_constraints i inner join INFORMATION_SCHEMA.key_column_usage ii " +
                "on i.CONSTRAINT_NAME = ii.CONSTRAINT_NAME";

            Assert.True(PgVirtualInterpreter.TryExecute(sql, EmptyCtx(), out var table));
            Assert.Equal(4, table.Columns.Count);
            Assert.Empty(table.Data);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FkCentric_TableSchema_query_returns_empty_rowset_with_6_columns()
        {
            const string sql =
                "select pkcol.COLUMN_NAME as PK_COLUMN_NAME, fkcol.TABLE_SCHEMA AS FK_TABLE_SCHEMA, " +
                "fkcol.TABLE_NAME AS FK_TABLE_NAME, fkcol.COLUMN_NAME as FK_COLUMN_NAME, " +
                "fkcol.ORDINAL_POSITION as ORDINAL, fkcon.CONSTRAINT_SCHEMA || '_' || fkcol.TABLE_NAME " +
                "from information_schema.key_column_usage pkcol " +
                "join information_schema.key_column_usage fkcol on 1=1 " +
                "join information_schema.table_constraints fkcon on 1=1";

            Assert.True(PgVirtualInterpreter.TryExecute(sql, EmptyCtx(), out var table));
            Assert.Equal(6, table.Columns.Count);
            Assert.Empty(table.Data);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void PkCentric_TableSchema_query_returns_empty_rowset_with_6_columns()
        {
            const string sql =
                "select pkcol.TABLE_SCHEMA AS PK_TABLE_SCHEMA, pkcol.TABLE_NAME AS PK_TABLE_NAME, " +
                "pkcol.COLUMN_NAME as PK_COLUMN_NAME, fkcol.COLUMN_NAME as FK_COLUMN_NAME, " +
                "fkcol.ORDINAL_POSITION as ORDINAL, fkcon.CONSTRAINT_SCHEMA || '_' || fkcol.TABLE_NAME " +
                "from information_schema.key_column_usage pkcol " +
                "join information_schema.key_column_usage fkcol on 1=1 " +
                "join information_schema.table_constraints fkcon on 1=1";

            Assert.True(PgVirtualInterpreter.TryExecute(sql, EmptyCtx(), out var table));
            Assert.Equal(6, table.Columns.Count);
            Assert.Empty(table.Data);
        }

        // ── Npgsql pg_catalog metadata empty-join shapes ──────────────────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void EnumTypes_query_returns_empty_rowset_with_2_columns()
        {
            const string sql =
                "SELECT pg_type.oid, enumlabel FROM pg_enum JOIN pg_type ON pg_type.oid = enumtypid ORDER BY oid";

            Assert.True(PgVirtualInterpreter.TryExecute(sql, EmptyCtx(), out var table));
            Assert.Equal(2, table.Columns.Count);
            Assert.Empty(table.Data);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CompositeTypes_query_returns_empty_rowset_with_3_columns()
        {
            const string sql =
                "SELECT typ.oid, att.attname, att.atttypid " +
                "FROM pg_type AS typ " +
                "JOIN pg_namespace AS ns ON ns.oid = typ.typnamespace " +
                "JOIN pg_class AS cls ON cls.oid = typ.typrelid " +
                "JOIN pg_attribute AS att ON att.attrelid = typ.typrelid " +
                "WHERE typ.typtype = 'c'";

            Assert.True(PgVirtualInterpreter.TryExecute(sql, EmptyCtx(), out var table));
            Assert.Equal(3, table.Columns.Count);
            Assert.Empty(table.Data);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Unknown_join_table_rejects_the_query()
        {
            const string sql =
                "SELECT pg_type.oid, enumlabel FROM pg_enum JOIN no_such.foo ON 1=1";

            Assert.False(PgVirtualInterpreter.TryExecute(sql, EmptyCtx(), out var table));
            Assert.Null(table);
        }

        private static VirtualQueryContext EmptyCtx() => new();

        private static string DecodeCell(Raven.Server.Integrations.PostgreSQL.Messages.PgTable table, int row, int column)
        {
            var cell = table.Data[row].ColumnData.Span[column];
            Assert.True(cell.HasValue);
            return Encoding.UTF8.GetString(cell.Value.Span);
        }
    }
}
