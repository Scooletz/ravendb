using System;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.Npgsql;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    /// <summary>
    /// Tests for <see cref="NpgsqlTypesQueryAstMatcher"/> — one test class covering all
    /// Npgsql type-loading family matchers as they are added (A, then B, then E, then C).
    /// </summary>
    public sealed class NpgsqlTypesQueryAstMatcherTests
    {
        // ── Family A — Modern nested (Npgsql 4.1.3–5.x+) ────────────────────────────────────────

        [Fact]
        public void FamilyA_Npgsql5TypesQuery_should_match()
        {
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql5TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql5TypesResponse, table);
        }

        [Fact]
        public void FamilyA_Npgsql4TypesQuery_should_match_same_response()
        {
            // Npgsql4TypesQuery differs from Npgsql5TypesQuery only by a leading \n.
            // Parser strips it → identical AST → same response object.
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql4TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql5TypesResponse, table);
        }

        [Fact]
        public void FamilyA_leading_newline_variant_should_match()
        {
            // Explicit check: prepending \n to Npgsql5TypesQuery must still match (mirrors Npgsql4).
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch("\n" + NpgsqlConfig.Npgsql5TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql5TypesResponse, table);
        }

        // ── Negative cases ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void FamilyA_flat_query_TypesQuery_should_not_match()
        {
            // Family C (TypesQuery / 4.0.1–4.0.12) is a flat query — must not be claimed.
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.TypesQuery, out _));
        }

        [Fact]
        public void FamilyA_subquery_in_from_but_wrong_shape_should_not_match()
        {
            // Has a RangeSubselect in FROM (would pass the old single-anchor check),
            // but only 2 projected columns and no .* wildcard — must not be claimed.
            const string query =
                "SELECT typname, oid FROM (SELECT typname, oid FROM pg_type WHERE typtype = 'b') AS base_types";
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(query, out _));
        }

        [Fact]
        public void FamilyA_subquery_in_from_three_targets_but_no_wildcard_should_not_match()
        {
            // Has a subquery + 3 targets, but no .* wildcard — still not Family A shape.
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

        // ── Family B — Mid flat (Npgsql 4.1.0–4.1.2) ────────────────────────────────────────────

        [Fact]
        public void FamilyB_Npgsql4_1_2TypesQuery_should_match()
        {
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql4_1_2TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql4_1_2TypesResponse, table);
        }

        [Fact]
        public void FamilyB_eight_column_TypesQuery_should_not_match()
        {
            // Family C has 8 columns (not 7) — must not be claimed by Family B matcher.
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.TypesQuery, out _));
        }

        [Fact]
        public void FamilyB_seven_column_query_without_typelem_should_not_match()
        {
            // Has 7 columns but lacks 'typelem' — must not be claimed.
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typbasetype, a.typtype, a.typalign, a.ord " +
                "FROM pg_type AS a JOIN pg_namespace AS ns ON ns.oid = a.typnamespace";
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(query, out _));
        }

        // ── Family E — Legacy Npgsql 3 (3.2.3–3.2.7) ────────────────────────────────────────────

        [Fact]
        public void FamilyE_Npgsql3TypesQuery_should_match()
        {
            Assert.True(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql3TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql3TypesResponse, table);
        }

        [Fact]
        public void FamilyE_TypesQuery_with_pg_class_should_not_match()
        {
            // Family C has pg_class in FROM — must not be claimed by Family E matcher.
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.TypesQuery, out _));
        }

        [Fact]
        public void FamilyE_Npgsql4_0_0_with_pg_class_should_not_match()
        {
            // Family D also has pg_class — must not be claimed.
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(NpgsqlConfig.Npgsql4_0_0TypesQuery, out _));
        }

        [Fact]
        public void FamilyE_eight_column_query_without_pg_proc_should_not_match()
        {
            // 8 columns and no pg_class, but pg_proc is also absent — must not be claimed.
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype, a.typtype, a.typelem, a.ord " +
                "FROM pg_type AS a JOIN pg_namespace AS ns ON ns.oid = a.typnamespace";
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(query, out _));
        }

        [Fact]
        public void FamilyE_eight_column_pg_type_pg_proc_no_pg_class_wrong_columns_should_not_match()
        {
            // Has 8 cols, pg_type, pg_proc, and no pg_class — but wrong projected column set
            // (typtype/typelem instead of the expected type/elemoid). Must not be claimed.
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype, a.typtype, a.typelem, a.ord " +
                "FROM pg_type AS a " +
                "JOIN pg_namespace AS ns ON ns.oid = a.typnamespace " +
                "JOIN pg_proc AS p ON p.oid = a.typreceive";
            Assert.False(NpgsqlTypesQueryAstMatcher.TryMatch(query, out _));
        }

        // ── Dispatch-level (through HardcodedQuery.TryParse; session=null is safe here) ──────────

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
    }
}
