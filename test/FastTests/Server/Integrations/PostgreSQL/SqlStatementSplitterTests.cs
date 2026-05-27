using Raven.Server.Integrations.PostgreSQL;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    public sealed class SqlStatementSplitterTests(ITestOutputHelper output) : NoDisposalNeeded(output)
    {
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Empty_input_returns_empty_list()
        {
            Assert.Empty(SqlStatementSplitter.Split(""));
            Assert.Empty(SqlStatementSplitter.Split("   \n\t "));
            Assert.Empty(SqlStatementSplitter.Split(null));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Single_statement_no_trailing_semicolon_returns_one_element()
        {
            var parts = SqlStatementSplitter.Split("SELECT 1");
            Assert.Single(parts);
            Assert.Equal("SELECT 1", parts[0]);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Single_statement_trailing_semicolon_is_normalized()
        {
            var parts = SqlStatementSplitter.Split("SELECT 1;");
            Assert.Single(parts);
            Assert.Equal("SELECT 1", parts[0]);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Multiple_statements_split_on_semicolons()
        {
            var parts = SqlStatementSplitter.Split("SET a=1; SET b=2; SELECT 3");
            Assert.Equal(3, parts.Count);
            Assert.Equal("SET a=1", parts[0]);
            Assert.Equal("SET b=2", parts[1]);
            Assert.Equal("SELECT 3", parts[2]);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Pgadmin_startup_probe_splits_into_four_pieces()
        {
            const string sql = "SET DateStyle=ISO; SET client_min_messages=notice; " +
                               "SELECT set_config('bytea_output','hex',false) FROM pg_show_all_settings() WHERE name = 'bytea_output'; " +
                               "SET client_encoding='utf-8';";
            var parts = SqlStatementSplitter.Split(sql);
            Assert.Equal(4, parts.Count);
            Assert.StartsWith("SET DateStyle", parts[0]);
            Assert.StartsWith("SET client_min_messages", parts[1]);
            Assert.StartsWith("SELECT set_config", parts[2]);
            Assert.StartsWith("SET client_encoding", parts[3]);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Semicolon_inside_single_quoted_string_is_not_a_separator()
        {
            var parts = SqlStatementSplitter.Split("SELECT 'a;b'; SELECT 'c'");
            Assert.Equal(2, parts.Count);
            Assert.Equal("SELECT 'a;b'", parts[0]);
            Assert.Equal("SELECT 'c'", parts[1]);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Semicolon_inside_double_quoted_identifier_is_not_a_separator()
        {
            var parts = SqlStatementSplitter.Split("SELECT \"a;b\"; SELECT \"c\"");
            Assert.Equal(2, parts.Count);
            Assert.Equal("SELECT \"a;b\"", parts[0]);
            Assert.Equal("SELECT \"c\"", parts[1]);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Semicolon_inside_line_comment_is_not_a_separator()
        {
            var parts = SqlStatementSplitter.Split("SELECT 1 -- a;b\n; SELECT 2");
            Assert.Equal(2, parts.Count);
            Assert.StartsWith("SELECT 1", parts[0]);
            Assert.Equal("SELECT 2", parts[1]);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Semicolon_inside_block_comment_is_not_a_separator()
        {
            var parts = SqlStatementSplitter.Split("SELECT /* a;b */ 1; SELECT 2");
            Assert.Equal(2, parts.Count);
            Assert.Equal("SELECT /* a;b */ 1", parts[0]);
            Assert.Equal("SELECT 2", parts[1]);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Escaped_single_quote_inside_string_does_not_close_the_literal()
        {
            // PG escapes a single quote inside a string by doubling it: 'it''s'.
            var parts = SqlStatementSplitter.Split("SELECT 'it''s; ok'; SELECT 2");
            Assert.Equal(2, parts.Count);
            Assert.Equal("SELECT 'it''s; ok'", parts[0]);
            Assert.Equal("SELECT 2", parts[1]);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Dollar_quoted_strings_protect_semicolons_within()
        {
            var parts = SqlStatementSplitter.Split("SELECT $tag$a;b;c$tag$; SELECT 2");
            Assert.Equal(2, parts.Count);
            Assert.Equal("SELECT $tag$a;b;c$tag$", parts[0]);
            Assert.Equal("SELECT 2", parts[1]);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Empty_statements_between_semicolons_are_dropped()
        {
            var parts = SqlStatementSplitter.Split("SELECT 1;;; SELECT 2;");
            Assert.Equal(2, parts.Count);
        }
    }
}
