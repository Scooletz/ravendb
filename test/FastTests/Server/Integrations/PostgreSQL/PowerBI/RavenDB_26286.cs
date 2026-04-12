using System;
using System.Reflection;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.PowerBI;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL.PowerBI
{
    /// <summary>
    /// Tests for RavenDB-26286: SQL-statement textbox support for PowerBI Fetch/Import and DirectQuery.
    /// <para>
    /// PowerBI Desktop wraps the user's SQL-textbox query inside an outer schema-probe subselect.
    /// These tests verify that the inner content can be SQL (not just RQL), and that translation
    /// to RQL is applied so the existing fetch/direct-query pipeline can continue normally.
    /// </para>
    /// </summary>
    public sealed class RavenDB_26286
    {
        // ── Regression ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Inner content is RQL: must still succeed unchanged (regression guard).
        /// </summary>
        [Fact]
        public void WrappedFetch_InnerRql_StillParsesCorrectly()
        {
            const string sql = @"select * from (from Employees) ""$Table"" limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("from Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

        // ── Fetch/Import – positive SQL-textbox cases ─────────────────────────────────

        /// <summary>
        /// Primary target: the exact shape PowerBI Desktop sends when the user types SQL into the
        /// statement textbox.  Inner content is a SQL SELECT with a string filter; outer is the
        /// standard schema-probe wrapper.
        /// </summary>
        [Fact]
        public void WrappedFetch_InnerSqlWithWhereFilter_TranslatesToRql()
        {
            const string sql =
                "select *\n" +
                "from\n" +
                "(\n" +
                "select \"FirstName\", \"LastName\", \"Address\"\n" +
                "from \"Employees\"\n" +
                "where \"Title\" = 'Sales Representative'\n" +
                ") \"_\"\n" +
                "limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sales Representative", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        /// <summary>
        /// Variation: SELECT * with a string equality filter on a different collection.
        /// Confirms the translation is not collection-specific.
        /// </summary>
        [Fact]
        public void WrappedFetch_InnerSqlStarProjection_TranslatesToRql()
        {
            const string sql =
                "select *\n" +
                "from\n" +
                "(\n" +
                "select *\n" +
                "from \"Orders\"\n" +
                "where \"Company\" = 'Companies/1-A'\n" +
                ") \"_\"\n" +
                "limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Companies/1-A", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        /// <summary>
        /// AND filter: two conditions joined with AND (PgSqlToRqlTranslator Easy_06 shape).
        /// </summary>
        [Fact]
        public void WrappedFetch_InnerSqlAndFilter_TranslatesToRql()
        {
            const string sql =
                "select *\n" +
                "from\n" +
                "(\n" +
                "select * from \"Orders\"\n" +
                "where \"Status\" = 'Pending' AND \"Freight\" > 10\n" +
                ") \"_\"\n" +
                "limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Pending", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        /// <summary>
        /// IN list: status must be one of a set of values (PgSqlToRqlTranslator Mid_13 shape).
        /// </summary>
        [Fact]
        public void WrappedFetch_InnerSqlInListFilter_TranslatesToRql()
        {
            const string sql =
                "select *\n" +
                "from\n" +
                "(\n" +
                "select * from \"Orders\"\n" +
                "where \"Status\" IN ('Pending', 'Shipped')\n" +
                ") \"_\"\n" +
                "limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Pending", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Shipped", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        /// <summary>
        /// IS NULL filter: field null check (PgSqlToRqlTranslator Mid_19 shape).
        /// </summary>
        [Fact]
        public void WrappedFetch_InnerSqlIsNullFilter_TranslatesToRql()
        {
            const string sql =
                "select *\n" +
                "from\n" +
                "(\n" +
                "select * from \"Orders\"\n" +
                "where \"ShippedAt\" IS NULL\n" +
                ") \"_\"\n" +
                "limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("null", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        // ── Fetch/Import – negative cases ─────────────────────────────────────────────

        /// <summary>
        /// Inner content that is neither valid RQL nor translatable SQL must be rejected cleanly.
        /// </summary>
        [Fact]
        public void WrappedFetch_InnerTextNeitherRqlNorSupportedSql_ReturnsFalse()
        {
            const string sql =
                "select *\n" +
                "from\n" +
                "(\n" +
                "GIBBERISH $$$ NOT VALID ANYTHING\n" +
                ") \"_\"\n" +
                "limit 0";

            Assert.False(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out _));
        }

        // ── DirectQuery – regression ──────────────────────────────────────────────────

        /// <summary>
        /// Existing DirectQuery inner RQL must still parse correctly after the shared-helper change.
        /// <para>
        /// SQL-textbox support for DirectQuery requires additional work beyond the shared helper:
        /// the current DirectQuery non-aggregate path requires GROUP BY presence in the outer wrapper,
        /// and the structural extractor only reaches the immediately innermost `) "_"` subquery (not
        /// the plain SQL SELECT nested inside the GROUP BY layer).  Full DirectQuery SQL-textbox
        /// support is left as a follow-up task.
        /// </para>
        /// </summary>
        [Fact]
        public void DirectQuery_InnerRql_StillParsesCorrectly_AfterSharedHelperChange()
        {
            const string sql =
                "select \"_\".\"Employee\"\n" +
                "from\n" +
                "(\n" +
                "    select \"rows\".\"Employee\" as \"Employee\"\n" +
                "    from\n" +
                "    (\n" +
                "        from Orders\n" +
                "        where Company in ('Companies/1-A', 'Companies/2-A')\n" +
                "    ) \"rows\"\n" +
                "    group by \"Employee\"\n" +
                ") \"_\"\n" +
                "order by \"_\".\"Employee\"\n" +
                "limit 501";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────

        private static string GetQueryString(PgQuery pgQuery)
        {
            return (string)typeof(PgQuery)
                .GetField("QueryString", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(pgQuery);
        }

        private static int? GetLimit(PgQuery pgQuery)
        {
            return (int?)typeof(RqlQuery)
                .GetField("_limit", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(pgQuery);
        }
    }
}
