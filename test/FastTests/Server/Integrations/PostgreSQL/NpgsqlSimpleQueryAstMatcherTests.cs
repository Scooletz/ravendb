using System;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.Npgsql;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    public sealed class NpgsqlSimpleQueryAstMatcherTests
    {
        // ---- version() ----

        [Fact]
        public void Version_canonical_lowercase_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("select version()", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [Fact]
        public void Version_uppercase_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("SELECT VERSION()", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [Fact]
        public void Version_with_extra_whitespace_should_match()
        {
            // The AST matcher is whitespace-tolerant; string matching would have failed here.
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("  select   version(  )  ", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [Fact]
        public void Version_with_leading_newline_should_match()
        {
            // Previously handled by Replace("\n","").Equals(...) — AST handles this naturally.
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("\nselect version()", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        // ---- current_setting('max_index_keys') ----

        [Fact]
        public void CurrentSetting_canonical_lowercase_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("select current_setting('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        [Fact]
        public void CurrentSetting_uppercase_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("SELECT CURRENT_SETTING('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        [Fact]
        public void CurrentSetting_with_extra_whitespace_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("  select  current_setting( 'max_index_keys' )  ", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        // ---- combined two-statement query ----

        [Fact]
        public void VersionAndCurrentSetting_combined_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch(
                "select version();select current_setting('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.VersionCurrentSettingResponse, table);
        }

        [Fact]
        public void VersionAndCurrentSetting_combined_with_whitespace_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch(
                "SELECT VERSION() ; SELECT CURRENT_SETTING('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.VersionCurrentSettingResponse, table);
        }

        // ---- CRLF and trailing-semicolon robustness ----

        [Fact]
        public void Version_with_crlf_line_endings_should_match()
        {
            // Windows-style line endings must not prevent matching.
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("select\r\nversion()", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [Fact]
        public void Version_with_trailing_semicolon_should_match()
        {
            // Some drivers append a trailing semicolon; the AST parser ignores it.
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("select version();", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [Fact]
        public void CurrentSetting_with_crlf_line_endings_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch(
                "select\r\ncurrent_setting('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        [Fact]
        public void CurrentSetting_with_trailing_semicolon_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch(
                "select current_setting('max_index_keys');", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        // ---- negative cases ----

        [Fact]
        public void Different_function_name_should_not_match()
        {
            // pg_version() is not a recognised query — must not be claimed.
            Assert.False(NpgsqlSimpleQueryAstMatcher.TryMatch("select pg_version()", out _));
        }

        [Fact]
        public void CurrentSetting_with_wrong_key_should_not_match()
        {
            // Only 'max_index_keys' is a known Npgsql initialisation key.
            Assert.False(NpgsqlSimpleQueryAstMatcher.TryMatch("select current_setting('some_other_key')", out _));
        }

        [Fact]
        public void Version_with_from_clause_should_not_match()
        {
            // Adding a FROM clause changes the semantics — must not match.
            Assert.False(NpgsqlSimpleQueryAstMatcher.TryMatch("select version() from pg_catalog.pg_settings", out _));
        }

        [Fact]
        public void Empty_query_should_not_match()
        {
            Assert.False(NpgsqlSimpleQueryAstMatcher.TryMatch("", out _));
            Assert.False(NpgsqlSimpleQueryAstMatcher.TryMatch("   ", out _));
        }

        // ---- dispatch-level (through HardcodedQuery.TryParse; session=null is safe here) ----

        [Fact]
        public void HardcodedQuery_Version_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse("select version()", Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_CurrentSetting_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(
                "select current_setting('max_index_keys')", Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_VersionAndCurrentSetting_combined_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(
                "select version();select current_setting('max_index_keys')", Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_Version_with_whitespace_variation_is_claimed_at_dispatch_level()
        {
            // This would have failed before with exact string matching.
            Assert.True(HardcodedQuery.TryParse("  SELECT  VERSION(  )  ", Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }
    }
}
