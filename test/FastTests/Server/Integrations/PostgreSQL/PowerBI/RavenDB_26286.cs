using System;
using System.Reflection;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.PowerBI;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL.PowerBI
{
    /// <summary>
    /// RavenDB-26286: SQL-statement textbox support for PowerBI Fetch/Import and DirectQuery.
    /// </summary>
    public sealed class RavenDB_26286
    {
        // ── Regression ────────────────────────────────────────────────────────────────

        [Fact]
        public void WrappedFetch_InnerRql_StillParsesCorrectly()
        {
            // Inner content is RQL — regression guard.
            const string sql = @"select * from (from Employees) ""$Table"" limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("from Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

        // ── Fetch/Import – positive SQL-textbox cases ─────────────────────────────────

        /// <summary>
        /// PowerBI Fetch SQL-textbox: inner SQL SELECT with a string filter.
        /// </summary>
        [Fact]
        public void WrappedFetch_InnerSqlWithWhereFilter_TranslatesToRql()
        {
            const string sql =
                @"select *
from
(
select ""FirstName"", ""LastName"", ""Address""
from ""Employees""
where ""Title"" = 'Sales Representative'
) ""_""
limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sales Representative", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        [Fact]
        public void WrappedFetch_InnerSqlStarProjection_TranslatesToRql()
        {
            const string sql =
                @"select *
from
(
select *
from ""Orders""
where ""Company"" = 'Companies/1-A'
) ""_""
limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Companies/1-A", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        /// <summary>AND filter.</summary>
        [Fact]
        public void WrappedFetch_InnerSqlAndFilter_TranslatesToRql()
        {
            const string sql =
                @"select *
from
(
select * from ""Orders""
where ""Status"" = 'Pending' AND ""Freight"" > 10
) ""_""
limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Pending", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        /// <summary>IN list filter.</summary>
        [Fact]
        public void WrappedFetch_InnerSqlInListFilter_TranslatesToRql()
        {
            const string sql =
                @"select *
from
(
select * from ""Orders""
where ""Status"" IN ('Pending', 'Shipped')
) ""_""
limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Pending", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Shipped", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        /// <summary>IS NULL filter.</summary>
        [Fact]
        public void WrappedFetch_InnerSqlIsNullFilter_TranslatesToRql()
        {
            const string sql =
                @"select *
from
(
select * from ""Orders""
where ""ShippedAt"" IS NULL
) ""_""
limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("null", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        // ── Fetch/Import – negative cases ─────────────────────────────────────────────

        [Fact]
        public void WrappedFetch_InnerTextNeitherRqlNorSupportedSql_ReturnsFalse()
        {
            const string sql =
                @"select *
from
(
GIBBERISH $$$ NOT VALID ANYTHING
) ""_""
limit 0";

            Assert.False(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out _));
        }

        // ── DirectQuery – SQL-textbox (simple non-aggregate) ─────────────────────────
        // 3-level wrapper: outer "_" → "rows" (with GROUP BY) → user SQL.

        [Fact]
        public void DirectQuery_InnerSql_SimpleSelectWithWhere_TranslatesToRql()
        {
            const string sql =
                @"select ""_"".""Title""
from
(
    select ""rows"".""Title"" as ""Title""
    from
    (
        select ""Title""
        from ""public"".""Employees""
        where ""Title"" = 'Sales Representative'
    ) ""rows""
    group by ""Title""
) ""_""
order by ""_"".""Title""
limit 501";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sales Representative", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectQuery_InnerSql_SingleColumnSelect_TranslatesToRql()
        {
            // Different collection (Orders); confirms translation is not collection-specific.
            const string sql =
                @"select ""_"".""Company""
from
(
    select ""rows"".""Company"" as ""Company""
    from
    (
        select ""Company""
        from ""public"".""Orders""
        where ""Company"" = 'Companies/1-A'
    ) ""rows""
    group by ""Company""
) ""_""
order by ""_"".""Company""
limit 501";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Companies/1-A", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectQuery_InnerSql_MultiColumn_TranslatesToRql()
        {
            // Two projected columns, AND filter.
            const string sql =
                @"select ""_"".""FirstName"",
       ""_"".""LastName""
from
(
    select ""rows"".""FirstName"" as ""FirstName"",
           ""rows"".""LastName"" as ""LastName""
    from
    (
        select ""FirstName"", ""LastName""
        from ""public"".""Employees""
        where ""Title"" = 'Sales Representative'
          and ""Country"" = 'USA'
    ) ""rows""
    group by ""FirstName"", ""LastName""
) ""_""
order by ""_"".""FirstName"", ""_"".""LastName""
limit 501";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sales Representative", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectQuery_InnerSql_NoWhereClause_TranslatesToRql()
        {
            // Simplest SQL-textbox shape: SELECT + FROM only, no WHERE filter.
            const string sql =
                @"select ""_"".""Company""
from
(
    select ""rows"".""Company"" as ""Company""
    from
    (
        select ""Company""
        from ""public"".""Orders""
    ) ""rows""
    group by ""Company""
) ""_""
order by ""_"".""Company""
limit 501";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectQuery_InnerSql_OrFilter_TranslatesToRql()
        {
            const string sql =
                @"select ""_"".""Status""
from
(
    select ""rows"".""Status"" as ""Status""
    from
    (
        select ""Status""
        from ""public"".""Orders""
        where ""Status"" = 'Pending' or ""Status"" = 'Shipped'
    ) ""rows""
    group by ""Status""
) ""_""
order by ""_"".""Status""
limit 501";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Pending", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Shipped", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectQuery_InnerSql_4LevelWrapper_TranslatesToRql()
        {
            // 4-level: outer "_" → "rows" (GROUP BY) → "$Table" pass-through → user SQL.
            // This exercises the "$Table" drill-through token that was already in the extractor
            // but had no SQL-textbox test.
            const string sql =
                @"select ""_"".""Company""
from
(
    select ""rows"".""Company"" as ""Company""
    from
    (
        select ""Company""
        from
        (
            select ""Company""
            from ""public"".""Orders""
            where ""Company"" = 'Companies/1-A'
        ) ""$Table""
    ) ""rows""
    group by ""Company""
) ""_""
order by ""_"".""Company""
limit 501";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Companies/1-A", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectQuery_InnerSql_SubqueryInFrom_ReturnsFalse()
        {
            // Inner SQL with a subquery in FROM — translator requires a plain collection RangeVar.
            const string sql =
                @"select ""_"".""X""
from
(
    select ""rows"".""X"" as ""X""
    from
    (
        select ""X"" from (select 1 as ""X"") ""sub""
    ) ""rows""
    group by ""X""
) ""_""
limit 501";

            Assert.False(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out _));
        }

        [Fact]
        public void DirectQuery_InnerSql_NoFromClause_ReturnsFalse()
        {
            // Inner SQL with no FROM — translator requires a collection source.
            const string sql =
                @"select ""_"".""X""
from
(
    select ""rows"".""X"" as ""X""
    from
    (
        select 1 as ""X""
    ) ""rows""
    group by ""X""
) ""_""
limit 501";

            Assert.False(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out _));
        }

        // ── DirectQuery – SQL-textbox grouped aggregate ───────────────────────────────

        [Fact]
        public void DirectQuery_GroupedAggregate_InnerSql_SumWithWhereAndGroupBy_TranslatesToRql()
        {
            const string sql =
                @"select ""_"".""Company"", ""_"".""a0""
from
(
    select ""rows"".""Company"" as ""Company"", sum(""rows"".""Freight"") as ""a0""
    from
    (
        select ""Company"", ""Freight""
        from ""public"".""Orders""
        where ""Status"" = 'Pending'
    ) ""rows""
    group by ""Company""
) ""_""
where not ""_"".""a0"" is null
order by ""_"".""a0"" desc
limit 1000001";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Pending", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sum", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Company", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectQuery_GroupedAggregate_InnerSql_MultiKeyGroupBy_TranslatesToRql()
        {
            // SUM with two group-by fields.
            const string sql =
                @"select ""_"".""Employee"", ""_"".""RequireAt"", ""_"".""a0""
from
(
    select ""rows"".""Employee"" as ""Employee"",
           ""rows"".""RequireAt"" as ""RequireAt"",
           sum(""rows"".""Freight"") as ""a0""
    from
    (
        select ""Employee"", ""RequireAt"", ""Freight""
        from ""public"".""Orders""
        where ""Company"" = 'Companies/1-A'
    ) ""rows""
    group by ""Employee"", ""RequireAt""
) ""_""
where not ""_"".""a0"" is null
order by ""_"".""a0"" desc
limit 1000001";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Companies/1-A", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sum", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Employee", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectQuery_GroupedAggregate_InnerSql_NoOuterWhereOrOrderBy_TranslatesToRql()
        {
            // SUM without outer WHERE (no not-null guard) and no ORDER BY.
            const string sql =
                @"select ""_"".""Company"", ""_"".""a0""
from
(
    select ""rows"".""Company"" as ""Company"", sum(""rows"".""Freight"") as ""a0""
    from
    (
        select ""Company"", ""Freight""
        from ""public"".""Orders""
    ) ""rows""
    group by ""Company""
) ""_""
limit 1000001";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sum", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectQuery_GroupedAggregate_InnerSql_OrderByGroupKey_TranslatesToRql()
        {
            // ORDER BY on the group key (Company), not the aggregate output (a0).
            const string sql =
                @"select ""_"".""Company"", ""_"".""a0""
from
(
    select ""rows"".""Company"" as ""Company"", sum(""rows"".""Freight"") as ""a0""
    from
    (
        select ""Company"", ""Freight""
        from ""public"".""Orders""
        where ""Status"" = 'Pending'
    ) ""rows""
    group by ""Company""
) ""_""
where not ""_"".""a0"" is null
order by ""_"".""Company"" asc
limit 1000001";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Pending", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sum", queryString, StringComparison.OrdinalIgnoreCase);
            // ORDER BY group key is emitted without "as double" modifier.
            Assert.DoesNotContain("as double", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectQuery_GroupedAggregate_InnerRql_StillParsesCorrectly()
        {
            // Regression guard: existing grouped-aggregate RQL-in-textbox must still work.
            const string sql =
                @"select ""_"".""Employee"", ""_"".""a0""
from
(
    select ""rows"".""Employee"" as ""Employee"", sum(""rows"".""Freight"") as ""a0""
    from
    (
        from Orders
        where Company in ('Companies/1-A', 'Companies/2-A')
    ) ""rows""
    group by ""Employee""
) ""_""
where not ""_"".""a0"" is null
limit 1000001";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sum", queryString, StringComparison.OrdinalIgnoreCase);
        }

        // ── DirectQuery – grouped aggregate – average (multi-aggregate limitation) ──────

        /// <summary>
        /// PowerBI "Average" visual sends sum + count together. Only sum is captured (first aggregate
        /// wins); count is silently dropped. This is a known limitation — multi-aggregate not supported.
        /// PowerBI computes average client-side from both columns, so returning sum only is insufficient.
        /// </summary>
        [Fact]
        public void DirectQuery_GroupedAverage_SumPlusCount_InnerSql_ParsesAsSumOnly()
        {
            // PowerBI sends both sum and count; we only capture sum (first aggregate wins).
            const string sql =
                @"select ""rows"".""Company"" as ""Company"",
    ""rows"".""Employee"" as ""Employee"",
    sum(""rows"".""Freight"") as ""a0"",
    count(""rows"".""Freight"") as ""a1""
from
(
    select *
    from Orders
    where Company in ('Companies/1-A', 'Companies/2-A', 'Companies/3-A')
) ""rows""
group by ""Company"",
    ""Employee""
limit 1000001";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            // Only sum is emitted — count column is silently dropped (multi-aggregate not supported).
            Assert.Contains("sum", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("count", queryString, StringComparison.OrdinalIgnoreCase);
        }

        // ── Identifier case recovery – Fetch/Import ──────────────────────────────────

        [Fact]
        public void WrappedFetch_UnquotedFieldNames_CasePreservedInRql()
        {
            const string sql =
                @"select *
from
(
select * from ""Orders""
where Company = 'Companies/1-A'
) ""_""
limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Company", queryString, StringComparison.Ordinal);
            Assert.DoesNotContain(" company ", queryString, StringComparison.Ordinal);
        }

        // ── Identifier case recovery – DirectQuery ────────────────────────────────────

        [Fact]
        public void DirectQuery_InnerSql_UnquotedFieldNames_CasePreservedInRql()
        {
            const string sql =
                @"select ""_"".""Company""
from
(
    select ""rows"".""Company"" as ""Company""
    from
    (
        select Company
        from ""public"".""Orders""
        where Company = 'Companies/1-A'
    ) ""rows""
    group by ""Company""
) ""_""
order by ""_"".""Company""
limit 501";

            Assert.True(PowerBIDirectQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);
            Assert.Contains("Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Company", queryString, StringComparison.Ordinal);
            Assert.DoesNotContain(" company ", queryString, StringComparison.Ordinal);
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
