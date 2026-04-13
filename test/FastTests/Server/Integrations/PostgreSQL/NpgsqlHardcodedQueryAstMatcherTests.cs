using System;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.Npgsql;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    // Tests for all Npgsql AST hardcoded-query matchers:
    // NpgsqlSimpleQueryAstMatcher, NpgsqlMetadataQueryAstMatcher, NpgsqlTypesQueryAstMatcher.
    public sealed class NpgsqlHardcodedQueryAstMatcherTests
    {
        // ── Simple queries — version() and current_setting(...) ──────────────────────────────────

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
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("  select   version(  )  ", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [Fact]
        public void Version_with_leading_newline_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("\nselect version()", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [Fact]
        public void Version_with_crlf_line_endings_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("select\r\nversion()", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [Fact]
        public void Version_with_trailing_semicolon_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("select version();", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

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

        [Fact]
        public void CurrentSetting_with_crlf_line_endings_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("select\r\ncurrent_setting('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        [Fact]
        public void CurrentSetting_with_trailing_semicolon_should_match()
        {
            Assert.True(NpgsqlSimpleQueryAstMatcher.TryMatch("select current_setting('max_index_keys');", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

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

        [Fact]
        public void Simple_different_function_name_should_not_match()
        {
            Assert.False(NpgsqlSimpleQueryAstMatcher.TryMatch("select pg_version()", out _));
        }

        [Fact]
        public void CurrentSetting_with_wrong_key_should_not_match()
        {
            Assert.False(NpgsqlSimpleQueryAstMatcher.TryMatch("select current_setting('some_other_key')", out _));
        }

        [Fact]
        public void Version_with_from_clause_should_not_match()
        {
            Assert.False(NpgsqlSimpleQueryAstMatcher.TryMatch("select version() from pg_catalog.pg_settings", out _));
        }

        [Fact]
        public void Simple_empty_query_should_not_match()
        {
            Assert.False(NpgsqlSimpleQueryAstMatcher.TryMatch("", out _));
            Assert.False(NpgsqlSimpleQueryAstMatcher.TryMatch("   ", out _));
        }

        // ── Metadata queries — enum types and composite types ─────────────────────────────────────

        [Fact]
        public void EnumTypes_block_comment_variant_should_match()
        {
            Assert.True(NpgsqlMetadataQueryAstMatcher.TryMatch(NpgsqlConfig.EnumTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.EnumTypesResponse, table);
        }

        [Fact]
        public void EnumTypes_line_comment_variant_should_match_same_response()
        {
            // Comments stripped by parser → AST identical to block-comment variant.
            Assert.True(NpgsqlMetadataQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql5EnumTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.EnumTypesResponse, table);
        }

        [Fact]
        public void EnumTypes_whitespace_variant_should_match()
        {
            const string query =
                "SELECT  pg_type.oid ,  enumlabel  " +
                "FROM  pg_enum  " +
                "JOIN  pg_type  ON  pg_type.oid = enumtypid  " +
                "ORDER BY  oid ,  enumsortorder";
            Assert.True(NpgsqlMetadataQueryAstMatcher.TryMatch(query, out var table));
            Assert.Same(NpgsqlConfig.EnumTypesResponse, table);
        }

        [Fact]
        public void EnumTypes_uppercase_keywords_should_match()
        {
            const string query =
                "SELECT PG_TYPE.OID, ENUMLABEL FROM PG_ENUM JOIN PG_TYPE ON PG_TYPE.OID=ENUMTYPID ORDER BY OID, ENUMSORTORDER";
            Assert.True(NpgsqlMetadataQueryAstMatcher.TryMatch(query, out var table));
            Assert.Same(NpgsqlConfig.EnumTypesResponse, table);
        }

        [Fact]
        public void CompositeTypes_block_comment_variant_should_match()
        {
            Assert.True(NpgsqlMetadataQueryAstMatcher.TryMatch(NpgsqlConfig.CompositeTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.CompositeTypesResponse, table);
        }

        [Fact]
        public void CompositeTypes_line_comment_variant_should_match()
        {
            Assert.True(NpgsqlMetadataQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql5CompositeTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.CompositeTypesResponse, table);
        }

        [Fact]
        public void CompositeTypes_old_orderby_variant_should_match()
        {
            // 4.0.0–4.0.3: ORDER BY typ.typname instead of typ.oid — AST matcher ignores ORDER BY.
            Assert.True(NpgsqlMetadataQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql4_0_0CompositeTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.CompositeTypesResponse, table);
        }

        [Fact]
        public void Metadata_empty_query_should_not_match()
        {
            Assert.False(NpgsqlMetadataQueryAstMatcher.TryMatch("", out _));
            Assert.False(NpgsqlMetadataQueryAstMatcher.TryMatch("   ", out _));
        }

        [Fact]
        public void EnumTypes_with_wrong_columns_should_not_match()
        {
            const string query =
                "SELECT pg_type.oid, enumlabel, enumsortorder FROM pg_enum JOIN pg_type ON pg_type.oid=enumtypid ORDER BY oid";
            Assert.False(NpgsqlMetadataQueryAstMatcher.TryMatch(query, out _));
        }

        [Fact]
        public void CompositeTypes_wrong_columns_should_not_match()
        {
            const string query =
                "SELECT typ.oid, att.attname FROM pg_type AS typ JOIN pg_class AS cls ON cls.oid = typ.typrelid JOIN pg_attribute AS att ON att.attrelid = typ.typrelid";
            Assert.False(NpgsqlMetadataQueryAstMatcher.TryMatch(query, out _));
        }

        [Fact]
        public void CompositeTypes_missing_pg_attribute_should_not_match()
        {
            const string query =
                "SELECT typ.oid, att.attname, att.atttypid FROM pg_type AS typ JOIN pg_class AS cls ON cls.oid = typ.typrelid WHERE typ.typtype = 'c'";
            Assert.False(NpgsqlMetadataQueryAstMatcher.TryMatch(query, out _));
        }

        [Fact]
        public void Unrelated_pg_catalog_query_should_not_match()
        {
            Assert.False(NpgsqlMetadataQueryAstMatcher.TryMatch(
                "SELECT oid, typname FROM pg_type WHERE typtype = 'b' ORDER BY oid", out _));
        }

        // ── Type-loading — Family A (modern nested, Npgsql 4.1.3–5.x+) ──────────────────────────

        [Fact]
        public void FamilyA_Npgsql5TypesQuery_should_match()
        {
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql5TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql5TypesResponse, table);
        }

        [Fact]
        public void FamilyA_Npgsql4TypesQuery_should_match_same_response()
        {
            // Differs from Npgsql5TypesQuery only by a leading \n → identical AST → same response.
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql4TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql5TypesResponse, table);
        }

        [Fact]
        public void FamilyA_leading_newline_variant_should_match()
        {
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch("\n" + NpgsqlConfig.Npgsql5TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql5TypesResponse, table);
        }

        [Fact]
        public void FamilyA_subquery_in_from_but_wrong_shape_should_not_match()
        {
            // Has a RangeSubselect in FROM but only 2 targets and no .* wildcard.
            const string query =
                "SELECT typname, oid FROM (SELECT typname, oid FROM pg_type WHERE typtype = 'b') AS base_types";
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(query, out _));
        }

        [Fact]
        public void FamilyA_subquery_in_from_three_targets_but_no_wildcard_should_not_match()
        {
            const string query =
                "SELECT a, b, c FROM (SELECT a, b, c FROM pg_type) AS sub JOIN pg_namespace AS ns ON true";
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(query, out _));
        }

        [Fact]
        public void FamilyA_empty_query_should_not_match()
        {
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch("", out _));
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch("   ", out _));
        }

        // ── Type-loading — Family B (mid flat, Npgsql 4.1.0–4.1.2) ──────────────────────────────

        [Fact]
        public void FamilyB_Npgsql4_1_2TypesQuery_should_match()
        {
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql4_1_2TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql4_1_2TypesResponse, table);
        }

        [Fact]
        public void FamilyB_seven_column_query_without_typelem_should_not_match()
        {
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typbasetype, a.typtype, a.typalign, a.ord " +
                "FROM pg_type AS a JOIN pg_namespace AS ns ON ns.oid = a.typnamespace";
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(query, out _));
        }

        // ── Type-loading — Family E (legacy Npgsql 3, 3.2.3–3.2.7) ─────────────────────────────

        [Fact]
        public void FamilyE_Npgsql3TypesQuery_should_match()
        {
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql3TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql3TypesResponse, table);
        }

        [Fact]
        public void FamilyE_eight_column_query_without_pg_proc_should_not_match()
        {
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype, a.typtype, a.typelem, a.ord " +
                "FROM pg_type AS a JOIN pg_namespace AS ns ON ns.oid = a.typnamespace";
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(query, out _));
        }

        [Fact]
        public void FamilyE_eight_column_pg_type_pg_proc_no_pg_class_wrong_columns_should_not_match()
        {
            // Has 8 cols + pg_type + pg_proc + no pg_class but wrong column names (typtype/typelem vs type/elemoid).
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype, a.typtype, a.typelem, a.ord " +
                "FROM pg_type AS a " +
                "JOIN pg_namespace AS ns ON ns.oid = a.typnamespace " +
                "JOIN pg_proc AS p ON p.oid = a.typreceive";
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(query, out _));
        }

        // ── Type-loading — Family C (old flat + pseudo-type arrays, Npgsql 4.0.1–4.0.12) ─────────

        [Fact]
        public void FamilyC_TypesQuery_should_match()
        {
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.TypesResponse, table);
        }

        [Fact]
        public void FamilyC_Npgsql4_0_3TypesQuery_should_match_same_response()
        {
            // Differs from TypesQuery only by leading comment/whitespace → identical AST → same response.
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql4_0_3TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.TypesResponse, table);
        }

        // ── Type-loading — Family D (old flat without pseudo-type arrays, Npgsql 4.0.0) ──────────

        [Fact]
        public void FamilyD_Npgsql4_0_0TypesQuery_should_match()
        {
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql4_0_0TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql4_0_0TypesResponse, table);
        }

        [Fact]
        public void FamilyD_TypesQuery_still_maps_to_C_not_D()
        {
            // Family C claims TypesQuery (pseudo-type branch present); Family D must not steal it.
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.TypesResponse, table);
        }

        [Fact]
        public void FamilyD_query_without_array_recv_block_should_not_match()
        {
            // Right shape but WHERE has no array_recv AND-block — positive structural anchor must fail.
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype, " +
                "a.typtype AS type, a.typelem AS elemoid, 0 AS ord " +
                "FROM pg_type AS a " +
                "JOIN pg_namespace AS ns ON ns.oid = a.typnamespace " +
                "JOIN pg_proc AS p ON p.oid = a.typreceive " +
                "LEFT JOIN pg_class AS cls ON cls.oid = a.typrelid " +
                "WHERE a.typtype IN ('b', 'r', 'e', 'd')";
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(query, out _));
        }

        // ── Dispatch-level (through HardcodedQuery.TryParse; session=null is safe here) ──────────

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
            Assert.True(HardcodedQuery.TryParse("  SELECT  VERSION(  )  ", Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_EnumTypes_block_comment_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.EnumTypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_EnumTypes_line_comment_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.Npgsql5EnumTypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_CompositeTypes_block_comment_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.CompositeTypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_CompositeTypes_line_comment_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.Npgsql5CompositeTypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_CompositeTypes_old_orderby_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.Npgsql4_0_0CompositeTypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_Npgsql5TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.Npgsql5TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_Npgsql4TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.Npgsql4TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_Npgsql4_1_2TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.Npgsql4_1_2TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_Npgsql3TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.Npgsql3TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_Npgsql4_0_3TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.Npgsql4_0_3TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [Fact]
        public void HardcodedQuery_Npgsql4_0_0TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlConfig.Npgsql4_0_0TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }
    }
}
