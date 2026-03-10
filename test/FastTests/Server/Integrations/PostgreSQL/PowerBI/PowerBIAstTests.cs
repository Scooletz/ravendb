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

    }
}
