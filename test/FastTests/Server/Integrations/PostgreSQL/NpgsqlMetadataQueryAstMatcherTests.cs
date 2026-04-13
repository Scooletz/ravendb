using System;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.Npgsql;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    public sealed class NpgsqlMetadataQueryAstMatcherTests
    {
        // ── Enum types ────────────────────────────────────────────────────────────────────────────

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

        // ── Composite types ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void CompositeTypes_block_comment_variant_should_match()
        {
            // 4.0.4–4.1.1 format: /*** ... ***/  ORDER BY typ.oid
            Assert.True(NpgsqlMetadataQueryAstMatcher.TryMatch(NpgsqlConfig.CompositeTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.CompositeTypesResponse, table);
        }

        [Fact]
        public void CompositeTypes_line_comment_variant_should_match()
        {
            // 4.1.3+ format: -- ...  ORDER BY typ.oid (same ORDER BY, different comment)
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

        // ── Negative cases ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Empty_query_should_not_match()
        {
            Assert.False(NpgsqlMetadataQueryAstMatcher.TryMatch("", out _));
            Assert.False(NpgsqlMetadataQueryAstMatcher.TryMatch("   ", out _));
        }

        [Fact]
        public void EnumTypes_with_wrong_columns_should_not_match()
        {
            // Projects the wrong column set — not a recognised enum query.
            const string query =
                "SELECT pg_type.oid, enumlabel, enumsortorder FROM pg_enum JOIN pg_type ON pg_type.oid=enumtypid ORDER BY oid";
            Assert.False(NpgsqlMetadataQueryAstMatcher.TryMatch(query, out _));
        }

        [Fact]
        public void CompositeTypes_wrong_columns_should_not_match()
        {
            // Right tables, wrong projection — must not be claimed.
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
            // A plausible pg_catalog query that references neither pg_enum nor pg_attribute.
            Assert.False(NpgsqlMetadataQueryAstMatcher.TryMatch(
                "SELECT oid, typname FROM pg_type WHERE typtype = 'b' ORDER BY oid", out _));
        }

        // ── Dispatch-level (through HardcodedQuery.TryParse; session=null is safe here) ──────────

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
    }
}
