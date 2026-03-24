using System;
using System.Collections.Generic;
using System.Reflection;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.PowerBI;
using Sparrow.Extensions;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL.PowerBI
{
    public sealed class PowerBIAstTests
    {
        [Fact]
        public void Simple_public_table_fetch_should_translate_via_ast_and_strip_table_alias_and_json_projection()
        {
            const string sql = @"select ""$Table"".""id()"" as ""id()"", ""$Table"".""Company"" as ""Company"", ""$Table"".""json()"" as ""json()""
from ""public"".""Orders"" ""$Table"" limit 200";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            var rqlQuery = Assert.IsType<PowerBIRqlQuery>(pgQuery);
            var queryString = GetQueryString(rqlQuery);

            Assert.NotNull(queryString);
            Assert.Contains("from 'Orders'", queryString);
            Assert.Contains("select", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("id()", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("$Table.", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("json()", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("limit 0, 200", queryString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TryParse_should_match_powerbi_all_collections_query_shape()
        {
            const string sql = "select TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE\n" +
                               "from INFORMATION_SCHEMA.tables\n" +
                               "where TABLE_SCHEMA not in ('information_schema', 'pg_catalog')\n" +
                               "order by TABLE_SCHEMA, TABLE_NAME";

            Assert.True(PowerBIAllCollectionsQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIAllCollectionsQuery>(pgQuery);
        }

        [Fact]
        public void TryParse_should_match_preview_columns_query_shape_and_extract_table_name()
        {
            const string sql = "select COLUMN_NAME, ORDINAL_POSITION, IS_NULLABLE, " +
                               "case when DATA_TYPE like '%char%' then DATA_TYPE else DATA_TYPE end as DATA_TYPE\n" +
                               "from INFORMATION_SCHEMA.columns\n" +
                               "where TABLE_SCHEMA = 'public' and TABLE_NAME = 'Regions'\n" +
                               "order by TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION";

            Assert.True(PowerBIPreviewQuery.TryParse(sql, documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIPreviewQuery>(pgQuery);
            Assert.Equal("from 'Regions'", GetQueryString(pgQuery));
        }

        [Fact]
        public void TryParse_should_match_wrapped_rql_fetch_shape_and_apply_outer_limit()
        {
            const string sql = "select * from (from Employees) \"$Table\" limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            Assert.Contains("from Employees", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

        [Fact]
        public void DirectQuery_desktop_null_order_helper_wrapper_should_be_handled_by_direct_query_parser_not_fetch()
        {
            const string sql = @"select ""_"".""OrderedAt"" as ""c2"", ""_"".""RequireAt"" as ""c3"" from ( select ""OrderedAt"", ""RequireAt"", ""_"".""t0_0"" as ""t0_0"", ""_"".""t1_0"" as ""t1_0"", ""_"".""t2_0"" as ""t2_0"", ""_"".""t3_0"" as ""t3_0"" from ( select ""_"".""OrderedAt"", ""_"".""RequireAt"", ""_"".""o0"", ""_"".""o1"", ""_"".""t0_0"", ""_"".""t1_0"", ""_"".""t2_0"", ""_"".""t3_0"" from ( select ""_"".""OrderedAt"" as ""OrderedAt"", ""_"".""RequireAt"" as ""RequireAt"", ""_"".""o0"" as ""o0"", ""_"".""o1"" as ""o1"", case when ""_"".""o0"" is not null then ""_"".""o0"" else timestamp '1899-12-28 00:00:00' end as ""t0_0"", case when ""_"".""o0"" is null then 0 else 1 end as ""t1_0"", case when ""_"".""o1"" is not null then ""_"".""o1"" else timestamp '1899-12-28 00:00:00' end as ""t2_0"", case when ""_"".""o1"" is null then 0 else 1 end as ""t3_0"" from ( select ""rows"".""OrderedAt"" as ""OrderedAt"", ""rows"".""RequireAt"" as ""RequireAt"", ""rows"".""o0"" as ""o0"", ""rows"".""o1"" as ""o1"" from ( select ""OrderedAt"" as ""OrderedAt"", ""RequireAt"" as ""RequireAt"", ""OrderedAt"" as ""o0"", ""RequireAt"" as ""o1"" from ( from Orders as o where o.Company = ""Companies/1-A"" OR o.Company = ""Companies/2-A"" select { OrderedAt: o.OrderedAt, RequireAt: o.RequireAt} ) ""$Table"" ) ""rows"" group by ""OrderedAt"", ""RequireAt"", ""o0"", ""o1"" ) ""_"" ) ""_"" ) ""_"" ) ""_"" order by ""_"".""t0_0"", ""_"".""t1_0"", ""_"".""t2_0"", ""_"".""t3_0"" limit 501";

            // Use the same PowerBI parsing entry point as production.
            Assert.True(PowerBIQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            // This assertion is expected to FAIL today (falls back to Fetch), making the mismatch explicit.
            Assert.IsType<PowerBIDirectQuery>(pgQuery);
        }

        [Fact]
        public void DirectQuery_distinct_list_wrapper_with_inner_rql_load_should_report_actual_parser_classification()
        {
            const string sql = @"select ""_"".""Name"",
        ""_"".""Manager""
from
(
    select ""rows"".""Name"" as ""Name"",
        ""rows"".""Manager"" as ""Manager""
    from
    (
        select ""Name"",
            ""Manager""
        from
        (
            from Employees as e
            where id() in ('employees/1-A')
            load e.ReportsTo as boss
            select { Name: e.FirstName, Manager: boss.FirstName }
        ) ""$Table""
    ) ""rows""
    group by ""Name"",
        ""Manager""
) ""_""
order by ""_"".""Name"",
        ""_"".""Manager""
limit 501";

            Assert.True(PowerBIQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIDirectQuery>(pgQuery);
        }

        [Fact]
        public void DirectQuery_desktop_grouped_sum_wrapper_should_be_handled_by_direct_query_parser_not_fetch()
        {
            const string sql = @"select ""_"".""Company"" as ""c2"",
    ""_"".""a0"" as ""a0""
from
(
    select ""_"".""Company"",
        ""_"".""a0""
    from
    (
        select ""_"".""Company"",
            ""_"".""a0""
        from
        (
            select ""rows"".""Company"" as ""Company"",
                sum(""rows"".""Freight"") as ""a0""
            from
            (
                select ""Company"",
                    ""Freight""
                from
                (
                    from Orders as o
                    where o.Company = ""Companies/1-A"" OR o.Company = ""Companies/2-A""
                    select { Company: o.Company, Freight: o.Freight}
                ) ""$Table""
            ) ""rows""
            group by ""Company""
        ) ""_""
        where not ""_"".""a0"" is null
    ) ""_""
) ""_""
order by ""_"".""a0"" desc,
        ""_"".""Company""
limit 1001";

            Assert.True(PowerBIQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));
            Assert.IsType<PowerBIDirectQuery>(pgQuery);
        }

        [Fact]
        public void TryParse_should_match_wrapped_rql_fetch_with_outer_where_not_equal_or_is_null()
        {
            const string sql = @"select ""_"".""id()"" as ""id()"", ""_"".""FirstName"" as ""FirstName""
from
(
    from Employees
) ""_""
where (""_"".""FirstName"" <> 'Anne' or ""_"".""FirstName"" is null)
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            var queryString = GetQueryString(pgQuery);
            Assert.Contains("from Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FirstName", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Anne", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("or", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("null", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

        [Fact]
        public void TryParse_should_translate_between_in_outer_where()
        {
            const string sql = @"select ""_"".""id()"" as ""id()"", ""_"".""OrderedAt"" as ""OrderedAt""
from
(
    from Orders
) ""_""
where ""_"".""OrderedAt"" between 1 and 10
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            var queryString = GetQueryString(pgQuery);
            Assert.Contains("from Orders", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OrderedAt", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">= 1", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<= 10", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

        [Fact]
        public void TryParse_should_merge_inner_where_with_outer_where()
        {
            const string sql = @"select ""_"".""id()"" as ""id()"", ""_"".""FirstName"" as ""FirstName""
from
(
    from Employees where startsWith(LastName, 'D')
) ""_""
where ""_"".""FirstName"" = 'Anne'
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            var queryString = GetQueryString(pgQuery);
            Assert.Contains("from Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("startsWith", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FirstName", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Anne", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

        [Fact]
        public void TryParse_should_translate_timestamp_literal_in_outer_where()
        {
            const string sql = @"select ""_"".""id()"" as ""id()"", ""_"".""HiredAt"" as ""HiredAt""
from
(
    from Employees
) ""_""
where ""_"".""HiredAt"" = timestamp '1994-11-15 00:00:00' and ""_"".""HiredAt"" is not null
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            var queryString = GetQueryString(pgQuery);
            Assert.Contains("from Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("HiredAt", queryString, StringComparison.OrdinalIgnoreCase);

            var expected = new DateTime(1994, 11, 15, 0, 0, 0).GetDefaultRavenFormat();
            Assert.Contains(expected, queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

        [Fact]
        public void TryParse_should_extract_single_replace_projection_via_ast()
        {
            const string sql = @"select ""_"".""id()"" as ""id()"", ""_"".""Title"" as ""Title"", replace(""_"".""Title"", 'Sales', 'Marketing') as ""t0_0""
from
(
    from Employees where startsWith(LastName, 'D') select Title
) ""_""
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            Assert.Contains("from Employees", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("startsWith", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));

            var replaces = GetReplaces(pgQuery);
            Assert.NotNull(replaces);
            Assert.True(replaces.TryGetValue("Title", out var r));
            Assert.Equal("t0_0", r.DstColumnName);
            Assert.Equal("Title", r.SrcColumnName);
            Assert.Equal("Sales", r.OldValue);
            Assert.Equal("Marketing", r.NewValue);
        }

        [Fact]
        public void TryParse_should_extract_multiple_replace_projections_via_ast()
        {
            const string sql = @"select ""_"".""id()"" as ""id()"",
    ""_"".""Title"" as ""Title"",
    replace(""_"".""Title"", 'Sales', 'Marketing') as ""t0_0"",
    replace(""_"".""FirstName"", 'Anne', 'Annie') as ""t0_1""
from
(
    from Employees where startsWith(LastName, 'D') select Title, FirstName
) ""_""
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            Assert.Contains("from Employees", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("startsWith", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));

            var replaces = GetReplaces(pgQuery);
            Assert.NotNull(replaces);

            Assert.True(replaces.TryGetValue("Title", out var titleReplace));
            Assert.Equal("t0_0", titleReplace.DstColumnName);
            Assert.Equal("Title", titleReplace.SrcColumnName);
            Assert.Equal("Sales", titleReplace.OldValue);
            Assert.Equal("Marketing", titleReplace.NewValue);

            Assert.True(replaces.TryGetValue("FirstName", out var firstNameReplace));
            Assert.Equal("t0_1", firstNameReplace.DstColumnName);
            Assert.Equal("FirstName", firstNameReplace.SrcColumnName);
            Assert.Equal("Anne", firstNameReplace.OldValue);
            Assert.Equal("Annie", firstNameReplace.NewValue);
        }

        [Fact]
        public void TryParse_should_support_multi_nested_wrapped_rql_with_multiple_wheres_and_replace()
        {
            const string sql = @"select ""_"".""id()"" as ""id()"", replace(""_"".""Title"", 'Sales', 'Marketing') as ""t0_0""
from
(
    select ""_"".""id()"" as ""id()"", ""_"".""Title"" as ""Title""
    from
    (
        from Employees where startsWith(LastName, 'D')
    ) ""_""
    where ""_"".""FirstName"" = 'Anne'
) ""_""
where ((""_"".""LastName"" <> 'Dodsworth' or ""_"".""LastName"" is null) and ""_"".""ReportsTo"" between 1 and 10)
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            var queryString = GetQueryString(pgQuery);
            Assert.Contains("from Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("startsWith", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Anne", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Dodsworth", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">= 1", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<= 10", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));

            var replaces = GetReplaces(pgQuery);
            Assert.NotNull(replaces);
            Assert.True(replaces.TryGetValue("Title", out var r));
            Assert.Equal("t0_0", r.DstColumnName);
        }

        [Fact]
        public void TryParse_should_support_deep_wrapper_nesting_with_complex_outer_where_and_multiple_replace_levels_and_huge_inner_rql()
        {
            var innerRql = @"from 'Users' as u
where u.Age > 0 and u.Name != null
order by u.Name
select {
    Name: u.Name,
    Age: u.Age,
    Title: u.Title,
    Field01: u.Field01,
    Field02: u.Field02,
    Field03: u.Field03,
    Field04: u.Field04,
    Field05: u.Field05,
    Field06: u.Field06,
    Field07: u.Field07,
    Field08: u.Field08,
    Field09: u.Field09,
    Field10: u.Field10,
    VeryLongTailMarker123: u.VeryLongTailMarker123
}";

            var sql = $@"select ""_"".""id()"",
       ""_"".""Company"",
       ""_"".""Employee"",
       ""_"".""Freight"",
       ""_"".""Lines"",
       ""_"".""OrderedAt"",
       ""_"".""RequireAt"",
       ""_"".""ShipTo"",
       ""_"".""ShipVia"",
       ""_"".""ShippedAt"",
       ""_"".""json()""
from
(
    select ""_"".""id()"" as ""id()"",
           ""_"".""Company"" as ""Company"",
           ""_"".""Employee"" as ""Employee"",
           ""_"".""Freight"" as ""Freight"",
           ""_"".""Lines"" as ""Lines"",
           ""_"".""OrderedAt"" as ""OrderedAt"",
           ""_"".""RequireAt"" as ""RequireAt"",
           ""_"".""ShipTo"" as ""ShipTo"",
           ""_"".""ShipVia"" as ""ShipVia"",
           ""_"".""ShippedAt"" as ""ShippedAt"",
           ""_"".""json()"" as ""json()"",
           ""_"".""FirstName"" as ""FirstName"",
           replace(""_"".""FirstName"", 'Ann', 'Anne') as ""t1_0""
    from
    (
        select ""$Table"".""id()"" as ""id()"",
               ""$Table"".""Company"" as ""Company"",
               ""$Table"".""Employee"" as ""Employee"",
               ""$Table"".""Freight"" as ""Freight"",
               ""$Table"".""Lines"" as ""Lines"",
               ""$Table"".""OrderedAt"" as ""OrderedAt"",
               ""$Table"".""RequireAt"" as ""RequireAt"",
               ""$Table"".""ShipTo"" as ""ShipTo"",
               ""$Table"".""ShipVia"" as ""ShipVia"",
               ""$Table"".""ShippedAt"" as ""ShippedAt"",
               ""$Table"".""json()"" as ""json()"",
               ""$Table"".""Title"" as ""Title"",
               replace(""$Table"".""Title"", 'Sales', 'Marketing') as ""t0_0""
        from
        (
            select ""_"".""id()"" as ""id()"",
                   ""_"".""Age"" as ""Age"",
                   ""_"".""Name"" as ""Name"",
                   ""_"".""Company"" as ""Company"",
                   ""_"".""Employee"" as ""Employee"",
                   ""_"".""Freight"" as ""Freight"",
                   ""_"".""Lines"" as ""Lines"",
                   ""_"".""OrderedAt"" as ""OrderedAt"",
                   ""_"".""RequireAt"" as ""RequireAt"",
                   ""_"".""ShipTo"" as ""ShipTo"",
                   ""_"".""ShipVia"" as ""ShipVia"",
                   ""_"".""ShippedAt"" as ""ShippedAt"",
                   ""_"".""json()"" as ""json()"",
                   ""_"".""Title"" as ""Title"",
                   ""_"".""DeletedAt"" as ""DeletedAt"",
                   ""_"".""IsActive"" as ""IsActive"",
                   ""_"".""Score"" as ""Score"",
                   ""_"".""FirstName"" as ""FirstName""
            from
            (
                {innerRql}
            ) ""_""
            where ((""_"".""Age"" between 10 and 20 and ""_"".""Name"" in ('a','b','c')) and (""_"".""DeletedAt"" is null))
        ) ""$Table""
        where (((not (""$Table"".""IsActive"" = true)) or (""$Table"".""Score"" <> 5)) and ""$Table"".""Title"" is not null)
    ) ""_""
    where (""_"".""FirstName"" is not null and ""_"".""Score"" <> 5)
) ""_""
where ((""_"".""Company"" is not null) and ((""_"".""Freight"" <> 5) or (""_"".""Freight"" is null)) and (""_"".""OrderedAt"" between 1 and 10))
limit 25";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);

            var expected = """
FROM Users AS u WHERE (((((u.Age > 0 AND u.Name != null) AND ((u.Company != null AND (u.Freight != 5 OR u.Freight = null)) AND (u.OrderedAt >= 1 AND u.OrderedAt <= 10))) AND (u.FirstName != null AND u.Score != 5)) AND ((u.IsActive != true OR u.Score != 5) AND u.Title != null)) AND (((u.Age >= 10 AND u.Age <= 20) AND u.Name IN ('a', 'b', 'c')) AND u.DeletedAt = null))
ORDER BY u.Name
SELECT { 

    Name: u.Name,
    Age: u.Age,
    Title: u.Title,
    Field01: u.Field01,
    Field02: u.Field02,
    Field03: u.Field03,
    Field04: u.Field04,
    Field05: u.Field05,
    Field06: u.Field06,
    Field07: u.Field07,
    Field08: u.Field08,
    Field09: u.Field09,
    Field10: u.Field10,
    VeryLongTailMarker123: u.VeryLongTailMarker123

}
""";
            Assert.Equal(Normalize(expected), Normalize(queryString));

            Assert.Equal(25, GetLimit(pgQuery));

            var replaces = GetReplaces(pgQuery);
            Assert.NotNull(replaces);

            Assert.True(replaces.TryGetValue("Title", out var titleReplace));
            Assert.Equal("t0_0", titleReplace.DstColumnName);
            Assert.Equal("Title", titleReplace.SrcColumnName);
            Assert.Equal("Sales", titleReplace.OldValue);
            Assert.Equal("Marketing", titleReplace.NewValue);

            Assert.True(replaces.TryGetValue("FirstName", out var firstNameReplace));
            Assert.Equal("t1_0", firstNameReplace.DstColumnName);
            Assert.Equal("FirstName", firstNameReplace.SrcColumnName);
            Assert.Equal("Ann", firstNameReplace.OldValue);
            Assert.Equal("Anne", firstNameReplace.NewValue);
        }

        [Fact]
        public void TryParse_should_support_declare_function_inner_rql_with_wrappers_outer_where_and_limit()
        {
            var innerRql = @"declare function isGood(u) { return u.Age > 18; }
from 'Users' as u
where isGood(u)
select { Name: u.Name, Age: u.Age }";

            var sql = $@"select ""_"".""id()"",
       ""_"".""Company"",
       ""_"".""Employee"",
       ""_"".""Freight"",
       ""_"".""Lines"",
       ""_"".""OrderedAt"",
       ""_"".""RequireAt"",
       ""_"".""ShipTo"",
       ""_"".""ShipVia"",
       ""_"".""ShippedAt"",
       ""_"".""json()""
from
(
    select ""$Table"".""id()"" as ""id()"",
           ""$Table"".""Company"" as ""Company"",
           ""$Table"".""Employee"" as ""Employee"",
           ""$Table"".""Freight"" as ""Freight"",
           ""$Table"".""Lines"" as ""Lines"",
           ""$Table"".""OrderedAt"" as ""OrderedAt"",
           ""$Table"".""RequireAt"" as ""RequireAt"",
           ""$Table"".""ShipTo"" as ""ShipTo"",
           ""$Table"".""ShipVia"" as ""ShipVia"",
           ""$Table"".""ShippedAt"" as ""ShippedAt"",
           ""$Table"".""json()"" as ""json()"",
           ""$Table"".""Name"" as ""Name"",
           replace(""$Table"".""Name"", 'a', 'b') as ""n0""
    from
    (
        select ""_"".""Name"" as ""Name""
        from
        (
            {innerRql}
        ) ""_""
    ) ""$Table""
    where (""$Table"".""Name"" in ('a','b') and ""$Table"".""Name"" is not null)
) ""_""
where (""_"".""Company"" in ('a','b') and ""_"".""Company"" is not null)
limit 7";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);

            var expected = """
DECLARE function isGood(u) { return u.Age > 18; }

FROM Users AS u WHERE ((isGood(u) AND (u.Company IN ('a', 'b') AND u.Company != null)) AND (u.Name IN ('a', 'b') AND u.Name != null))
SELECT { 
 Name: u.Name, Age: u.Age 
}
""";
            Assert.Equal(Normalize(expected), Normalize(queryString));
            Assert.Equal(7, GetLimit(pgQuery));

            var replaces = GetReplaces(pgQuery);
            Assert.NotNull(replaces);
            Assert.True(replaces.TryGetValue("Name", out var nameReplace));
            Assert.Equal("n0", nameReplace.DstColumnName);
            Assert.Equal("Name", nameReplace.SrcColumnName);
            Assert.Equal("a", nameReplace.OldValue);
            Assert.Equal("b", nameReplace.NewValue);
        }

        [Fact]
        public void TryParse_should_support_projection_heavy_multi_wrapper_with_replace_at_outer_and_inner_levels_and_complex_outer_where()
        {
            var innerRql = @"from Orders as o
where o.Company != null
select { Company: o.Company, Employee: o.Employee, Freight: o.Freight, OrderedAt: o.OrderedAt, ShippedAt: o.ShippedAt }";

            const int limit = 33;

            var sql = $@"select ""_"".""id()"",
       ""_"".""Company"",
       ""_"".""Employee"",
       replace(""_"".""Employee"", 'X', 'Y') as ""tOuter_0"",
       ""_"".""Freight"",
       ""_"".""Lines"",
       ""_"".""OrderedAt"",
       ""_"".""RequireAt"",
       ""_"".""ShipTo"",
       ""_"".""ShipVia"",
       ""_"".""ShippedAt"",
       ""_"".""json()""
from
(
    select ""_"".""id()"" as ""id()"",
           ""_"".""Company"" as ""Company"",
           ""_"".""Employee"" as ""Employee"",
           ""_"".""Freight"" as ""Freight"",
           ""_"".""Lines"" as ""Lines"",
           ""_"".""OrderedAt"" as ""OrderedAt"",
           ""_"".""RequireAt"" as ""RequireAt"",
           ""_"".""ShipTo"" as ""ShipTo"",
           ""_"".""ShipVia"" as ""ShipVia"",
           ""_"".""ShippedAt"" as ""ShippedAt"",
           ""_"".""json()"" as ""json()""
    from
    (
        select ""$Table"".""id()"" as ""id()"",
               ""$Table"".""Company"" as ""Company"",
               replace(""$Table"".""Company"", 'A', 'B') as ""tInner_0"",
               ""$Table"".""Employee"" as ""Employee"",
               ""$Table"".""Freight"" as ""Freight"",
               ""$Table"".""Lines"" as ""Lines"",
               ""$Table"".""OrderedAt"" as ""OrderedAt"",
               ""$Table"".""RequireAt"" as ""RequireAt"",
               ""$Table"".""ShipTo"" as ""ShipTo"",
               ""$Table"".""ShipVia"" as ""ShipVia"",
               ""$Table"".""ShippedAt"" as ""ShippedAt"",
               ""$Table"".""json()"" as ""json()""
        from
        (
            {innerRql}
        ) ""$Table""
    ) ""_""
) ""_""
where (((""_"".""Employee"" <> 'X' or ""_"".""Employee"" is null) and (""_"".""ShippedAt"" is not null)) and (""_"".""Freight"" between 10 and 20))
limit {limit}";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);

            var expected = """
FROM Orders AS o WHERE (o.Company != null AND (((o.Employee != 'X' OR o.Employee = null) AND o.ShippedAt != null) AND (o.Freight >= 10 AND o.Freight <= 20)))
SELECT { 
 Company: o.Company, Employee: o.Employee, Freight: o.Freight, OrderedAt: o.OrderedAt, ShippedAt: o.ShippedAt 
}
""";
            Assert.Equal(Normalize(expected), Normalize(queryString));
            Assert.Equal(limit, GetLimit(pgQuery));

            var replaces = GetReplaces(pgQuery);
            Assert.NotNull(replaces);

            Assert.True(replaces.TryGetValue("Employee", out var employeeReplace));
            Assert.Equal("tOuter_0", employeeReplace.DstColumnName);
            Assert.Equal("Employee", employeeReplace.SrcColumnName);
            Assert.Equal("X", employeeReplace.OldValue);
            Assert.Equal("Y", employeeReplace.NewValue);

            Assert.True(replaces.TryGetValue("Company", out var companyReplace));
            Assert.Equal("tInner_0", companyReplace.DstColumnName);
            Assert.Equal("Company", companyReplace.SrcColumnName);
            Assert.Equal("A", companyReplace.OldValue);
            Assert.Equal("B", companyReplace.NewValue);
        }

        [Fact]
        public void TryParse_should_support_projection_heavy_wrapper_with_large_inner_rql_and_in_and_not_outer_where_and_limit()
        {
            var innerRql = @"from 'Users' as u
where u.Age > 0
order by u.Name
select {
    Name: u.Name,
    Age: u.Age,
    Company: u.Company,
    Employee: u.Employee,
    Freight: u.Freight,
    Lines: u.Lines,
    OrderedAt: u.OrderedAt,
    RequireAt: u.RequireAt,
    ShipTo: u.ShipTo,
    ShipVia: u.ShipVia,
    ShippedAt: u.ShippedAt,
    TailMarkerB987: u.TailMarkerB987
}";

            const int limit = 12;

            var sql = $@"select ""_"".""id()"",
       ""_"".""Company"",
       ""_"".""Employee"",
       ""_"".""Freight"",
       ""_"".""Lines"",
       ""_"".""OrderedAt"",
       ""_"".""RequireAt"",
       ""_"".""ShipTo"",
       ""_"".""ShipVia"",
       ""_"".""ShippedAt"",
       ""_"".""json()""
from
(
    select ""_"".""id()"" as ""id()"",
           ""_"".""Company"" as ""Company"",
           ""_"".""Employee"" as ""Employee"",
           ""_"".""Freight"" as ""Freight"",
           ""_"".""Lines"" as ""Lines"",
           ""_"".""OrderedAt"" as ""OrderedAt"",
           ""_"".""RequireAt"" as ""RequireAt"",
           ""_"".""ShipTo"" as ""ShipTo"",
           ""_"".""ShipVia"" as ""ShipVia"",
           ""_"".""ShippedAt"" as ""ShippedAt"",
           ""_"".""json()"" as ""json()""
    from
    (
        {innerRql}
    ) ""_""
) ""_""
where ((""_"".""Company"" in ('a','b','c')) and (not (""_"".""Company"" in ('x','y'))))
limit {limit}";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            var queryString = GetQueryString(pgQuery);
            Assert.NotNull(queryString);

            var expected = """
FROM Users AS u WHERE (u.Age > 0 AND (u.Company IN ('a', 'b', 'c') AND NOT (u.Company IN ('x', 'y'))))
ORDER BY u.Name
SELECT { 

    Name: u.Name,
    Age: u.Age,
    Company: u.Company,
    Employee: u.Employee,
    Freight: u.Freight,
    Lines: u.Lines,
    OrderedAt: u.OrderedAt,
    RequireAt: u.RequireAt,
    ShipTo: u.ShipTo,
    ShipVia: u.ShipVia,
    ShippedAt: u.ShippedAt,
    TailMarkerB987: u.TailMarkerB987

}
""";
            Assert.Equal(Normalize(expected), Normalize(queryString));
            Assert.Equal(limit, GetLimit(pgQuery));
        }

        [Fact]
        public void TryParse_should_extract_nested_replace_projection_aliases_as_keys()
        {
            const string sql = @"select ""_"".""id()"" as ""id()"",
    ""_"".""LastName"" as ""LastName"",
    ""_"".""FirstName"" as ""FirstName"",
    ""_"".""json()"" as ""json()"",
    ""_"".""t0_0"" as ""t0_0"",
    ""_"".""t0_03"" as ""t0_03"",
    replace(""_"".""t0_0"", 'aaa', 'bbb') as ""t0_02"",
    replace(""_"".""t0_03"", 'Steven', 'ddd') as ""t0_04""
from
(
    select ""_"".""id()"" as ""id()"",
        ""_"".""LastName"" as ""LastName"",
        ""_"".""FirstName"" as ""FirstName"",
        ""_"".""json()"" as ""json()"",
        replace(""_"".""LastName"", 'Dodsworth', 'aaa') as ""t0_0"",
        replace(""_"".""FirstName"", 'Janet', 'ccc') as ""t0_03""
    from
    (
        from Employees
    ) ""_""
) ""_""
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            Assert.Equal(1000, GetLimit(pgQuery));

            var replaces = GetReplaces(pgQuery);
            Assert.NotNull(replaces);

            Assert.True(replaces.TryGetValue("LastName", out var r0));
            Assert.Equal("t0_0", r0.DstColumnName);
            Assert.Equal("LastName", r0.SrcColumnName);
            Assert.Equal("Dodsworth", r0.OldValue);
            Assert.Equal("aaa", r0.NewValue);

            Assert.True(replaces.TryGetValue("FirstName", out var r03));
            Assert.Equal("t0_03", r03.DstColumnName);
            Assert.Equal("FirstName", r03.SrcColumnName);
            Assert.Equal("Janet", r03.OldValue);
            Assert.Equal("ccc", r03.NewValue);

            Assert.True(replaces.TryGetValue("t0_0", out var r02));
            Assert.Equal("t0_02", r02.DstColumnName);
            Assert.Equal("t0_0", r02.SrcColumnName);
            Assert.Equal("aaa", r02.OldValue);
            Assert.Equal("bbb", r02.NewValue);

            Assert.True(replaces.TryGetValue("t0_03", out var r04));
            Assert.Equal("t0_04", r04.DstColumnName);
            Assert.Equal("t0_03", r04.SrcColumnName);
            Assert.Equal("Steven", r04.OldValue);
            Assert.Equal("ddd", r04.NewValue);
        }

        [Fact]
        public void TryParse_should_match_wrapped_rql_fetch_shape_with_outer_where_and_apply_filter_to_inner_alias()
        {
            const string sql = @"select ""_"".""id()"",
    ""_"".""FirstName"",
    ""_"".""json()""
from
(
    from Employees as e
) ""_""
where ""_"".""FirstName"" = 'Anne' and ""_"".""FirstName"" is not null
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            Assert.Contains("from Employees", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("where", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FirstName", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Anne", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

        [Fact]
        public void TryParse_should_match_wrapped_rql_fetch_with_outer_where_swapped_predicate_order()
        {
            const string sql = @"select ""_"".""id()"",
    ""_"".""FirstName"",
    ""_"".""json()""
from
(
    from Employees as e
) ""_""
where ""_"".""FirstName"" is not null and ""_"".""FirstName"" = 'Anne'
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            Assert.Contains("from Employees", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("where", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FirstName", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Anne", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

        [Fact]
        public void TryParse_should_match_wrapped_rql_fetch_with_outer_where_or_nesting()
        {
            const string sql = @"select ""_"".""id()"",
    ""_"".""FirstName"",
    ""_"".""Title""
from
(
    from Employees as e
) ""_""
where ((""_"".""FirstName"" <> 'Anne' or ""_"".""FirstName"" is null) and (""_"".""Title"" = 'Vice President, Sales' and ""_"".""Title"" is not null))
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);

            var queryString = GetQueryString(pgQuery);
            Assert.Contains("from Employees", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("where", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FirstName", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Anne", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Title", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Vice President, Sales", queryString, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

        [Fact]
        public void TryParse_should_match_wrapped_rql_fetch_shape_with_underscore_alias_and_limit_0()
        {
            const string sql = "select * from (from Orders) \"_\" limit 0";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            Assert.Contains("from Orders", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, GetLimit(pgQuery));
        }

        [Fact]
        public void TryParse_should_match_projected_wrapped_rql_fetch_shape_and_apply_outer_limit()
        {
            const string sql = @"select ""$Table"".""id()"" as ""id()"",
       ""$Table"".""LastName"" as ""LastName"",
       ""$Table"".""FirstName"" as ""FirstName"",
       ""$Table"".""json()"" as ""json()""
from
(
    from Employees
) ""$Table""
limit 1000";

            Assert.True(PowerBIFetchQuery.TryParse(sql, Array.Empty<int>(), documentDatabase: null, out var pgQuery));

            Assert.IsType<PowerBIRqlQuery>(pgQuery);
            Assert.Contains("from Employees", GetQueryString(pgQuery), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000, GetLimit(pgQuery));
        }

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

        private static Dictionary<string, ReplaceColumnValue> GetReplaces(PgQuery pgQuery)
        {
            return (Dictionary<string, ReplaceColumnValue>)typeof(PowerBIRqlQuery)
                .GetField("_replaces", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(pgQuery);
        }

        private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();

    }
}
