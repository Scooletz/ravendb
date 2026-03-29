#if NET8_0
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Xunit;

namespace EmbeddedTests.Server.Integrations.PostgreSQL;

public class RavenDB_17503 : PostgreSqlIntegrationTestBase
{
    [Fact]
    public async Task DirectQuery_distinct_list_wrapper_with_inner_rql_load_two_columns_should_work_end_to_end()
    {
        // Ownership note:
        // - `FastTests.Server.Integrations.PostgreSQL.PowerBI.PowerBIAstTests.DirectQuery_distinct_list_wrapper_with_inner_rql_load_should_report_actual_parser_classification`
        //   asserts parser classification (`PowerBIDirectQuery`).
        // - This test asserts end-to-end execution and result shape.
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

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

            var result = await Act(store, sql);

            Assert.NotNull(result);

            Assert.True(result.Columns.Contains("Name"));
            Assert.True(result.Columns.Contains("Manager"));

            Assert.Equal(1, result.Rows.Count);

            var name = result.Rows[0]["Name"];
            var manager = result.Rows[0]["Manager"];

            Assert.NotEqual(System.DBNull.Value, name);
            Assert.NotEqual(System.DBNull.Value, manager);
        }

    }

    [Fact]
    public async Task DirectQuery_distinct_list_wrapper_single_column_employee_should_return_exactly_one_column_end_to_end()
    {
        // Ownership note:
        // - `FastTests.Server.Integrations.PostgreSQL.PowerBI.PowerBIAstTests.DirectQuery_distinct_list_wrapper_single_column_orders_employee_should_be_classified_as_direct_query`
        //   asserts parser classification (`PowerBIDirectQuery`).
        // - This test asserts end-to-end execution and result shape (exactly one column).
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select ""_"".""Employee""
from 
(
    select ""rows"".""Employee"" as ""Employee""
    from 
    (
        select ""Employee""
        from 
        (
            from Orders
            where Company in ('Companies/1-A', 'Companies/2-A', 'Companies/3-A')
        ) ""$Table""
    ) ""rows""
    group by ""Employee""
) ""_""
order by ""_"".""Employee""
limit 1001";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);

            Assert.True(result.Columns.Contains("Employee"));
            Assert.Equal(1, result.Columns.Count);
        }
    }

    [Fact]
    public async Task DirectQuery_desktop_employee_requireAt_json_with_null_order_helper_columns_should_return_exactly_three_columns_end_to_end()
    {
        // Ownership note:
        // - `FastTests.Server.Integrations.PostgreSQL.PowerBI.PowerBIAstTests.DirectQuery_desktop_employee_requireAt_json_with_null_order_helper_columns_should_be_classified_as_direct_query`
        //   asserts parser classification (`PowerBIDirectQuery`).
        // - This test asserts end-to-end execution and exact result shape (3 columns).
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select ""_"".""Employee"" as ""c3"",
    ""_"".""RequireAt"" as ""c7"",
    ""_"".""json()"" as ""c11""
from 
(
    select ""Employee"",
        ""RequireAt"",
        ""json()"",
        ""_"".""t2_0"" as ""t2_0"",
        ""_"".""t3_0"" as ""t3_0""
    from 
    (
        select ""_"".""Employee"",
            ""_"".""RequireAt"",
            ""_"".""json()"",
            ""_"".""o2"",
            ""_"".""t2_0"",
            ""_"".""t3_0""
        from 
        (
            select ""_"".""Employee"" as ""Employee"",
                ""_"".""RequireAt"" as ""RequireAt"",
                ""_"".""json()"" as ""json()"",
                ""_"".""o2"" as ""o2"",
                case
                    when ""_"".""o2"" is not null
                    then ""_"".""o2""
                    else timestamp '1899-12-28 00:00:00'
                end as ""t2_0"",
                case
                    when ""_"".""o2"" is null
                    then 0
                    else 1
                end as ""t3_0""
            from 
            (
                select ""rows"".""Employee"" as ""Employee"",
                    ""rows"".""RequireAt"" as ""RequireAt"",
                    ""rows"".""json()"" as ""json()"",
                    ""rows"".""o2"" as ""o2""
                from 
                (
                    select ""Employee"" as ""Employee"",
                        ""RequireAt"" as ""RequireAt"",
                        ""json()"" as ""json()"",
                        ""RequireAt"" as ""o2""
                    from 
                    (
                        from Orders
                        where Company in ('Companies/1-A', 'Companies/2-A', 'Companies/3-A')
                    ) ""$Table""
                ) ""rows""
                group by ""Employee"",
                    ""RequireAt"",
                    ""json()"",
                    ""o2""
            ) ""_""
        ) ""_""
    ) ""_""
) ""_""
order by ""_"".""Employee"",
        ""_"".""json()"",
        ""_"".""t2_0"",
        ""_"".""t3_0""
limit 501";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);

            Assert.True(result.Columns.Contains("Employee"));
            Assert.True(result.Columns.Contains("RequireAt"));
            Assert.True(result.Columns.Contains("json()"));
            Assert.Equal(3, result.Columns.Count);
        }
    }

    [Fact]
    public async Task DirectQuery_distinct_list_wrapper_single_column_should_work_end_to_end()
    {
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select ""_"".""Name""
from 
(
    select ""rows"".""Name"" as ""Name""
    from 
    (
        select ""Name""
        from 
        (
            declare function name(e) {
    return e.FirstName + "" "" + e.LastName
}
from Employees as e
where id() in ('employees/1-A'  )
select { Name: name(e)}
        ) ""$Table""
    ) ""rows""
    group by ""Name""
) ""_""
order by ""_"".""Name""
limit 501";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);
            Assert.Equal(1, result.Rows.Count);
            Assert.Equal("Nancy Davolio", result.Rows[0]["Name"]);
        }
    }

    [Fact]
    public async Task DirectQuery_distinct_list_wrapper_two_columns_should_work_end_to_end()
    {
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select ""_"".""Name"",
        ""_"".""Title""
    from
    (
        select ""rows"".""Name"" as ""Name"",
            ""rows"".""Title"" as ""Title""
        from
        (
            select ""Name"",
                ""Title""
            from
            (
                declare function name(e) {
        return e.FirstName + "" "" + e.LastName
    }
    from Employees as e
    where id() in ('employees/1-A','employees/2-A','employees/3-A')
    select { Name: name(e), Title: e.Title}
            ) ""$Table""
        ) ""rows""
        group by ""Name"",
            ""Title""
    ) ""_""
    order by ""_"".""Name"",
            ""_"".""Title""
    limit 501";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);
            Assert.Equal(3, result.Rows.Count);

            Assert.True(result.Columns.Contains("Name"));
            Assert.True(result.Columns.Contains("Title"));

            var actual = new HashSet<(string Name, string Title)>();
            foreach (DataRow row in result.Rows)
            {
                Assert.True(row.Table.Columns.Contains("Name"));
                Assert.True(row.Table.Columns.Contains("Title"));

                Assert.NotNull(row["Name"]);
                Assert.NotNull(row["Title"]);

                Assert.NotEqual(System.DBNull.Value, row["Name"]);
                Assert.NotEqual(System.DBNull.Value, row["Title"]);

                actual.Add(((string)row["Name"], (string)row["Title"]));
            }

            Assert.Contains(("Nancy Davolio", "Sales Representative"), actual);
            Assert.Contains(("Andrew Fuller", "Vice President, Sales"), actual);
            Assert.Contains(("Janet Leverling", "Sales Representative"), actual);
        }
    }

    [Fact]
    public async Task DirectQuery_distinct_list_wrapper_order_by_null_helper_columns_should_work_end_to_end()
    {
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select ""_"".""OrderedAt"" as ""c6"",
    ""_"".""RequireAt"" as ""c7""
from
(
    select ""OrderedAt"",
        ""RequireAt"",
        ""_"".""t0_0"" as ""t0_0"",
        ""_"".""t1_0"" as ""t1_0"",
        ""_"".""t2_0"" as ""t2_0"",
        ""_"".""t3_0"" as ""t3_0""
    from
    (
        select ""_"".""OrderedAt"",
            ""_"".""RequireAt"",
            ""_"".""o0"",
            ""_"".""o1"",
            ""_"".""t0_0"",
            ""_"".""t1_0"",
            ""_"".""t2_0"",
            ""_"".""t3_0""
        from
        (
            select ""_"".""OrderedAt"" as ""OrderedAt"",
                ""_"".""RequireAt"" as ""RequireAt"",
                ""_"".""o0"" as ""o0"",
                ""_"".""o1"" as ""o1"",
                case
                    when ""_"".""o0"" is not null
                    then ""_"".""o0""
                    else timestamp '1899-12-28 00:00:00'
                end as ""t0_0"",
                case
                    when ""_"".""o0"" is null
                    then 0
                    else 1
                end as ""t1_0"",
                case
                    when ""_"".""o1"" is not null
                    then ""_"".""o1""
                    else timestamp '1899-12-28 00:00:00'
                end as ""t2_0"",
                case
                    when ""_"".""o1"" is null
                    then 0
                    else 1
                end as ""t3_0""
            from
            (
                select ""rows"".""OrderedAt"" as ""OrderedAt"",
                    ""rows"".""RequireAt"" as ""RequireAt"",
                    ""rows"".""o0"" as ""o0"",
                    ""rows"".""o1"" as ""o1""
                from
                (
                    select ""OrderedAt"" as ""OrderedAt"",
                        ""RequireAt"" as ""RequireAt"",
                        ""RequireAt"" as ""o0"",
                        ""OrderedAt"" as ""o1""
                    from
                    (
                        from Orders as o
                        where o.Company = ""Companies/1-A"" OR o.Company = ""Companies/2-A""
                    ) ""$Table""
                ) ""rows""
                group by ""OrderedAt"",
                    ""RequireAt"",
                    ""o0"",
                    ""o1""
            ) ""_""
        ) ""_""
    ) ""_""
) ""_""
order by ""_"".""t0_0"",
        ""_"".""t1_0"",
        ""_"".""t2_0"",
        ""_"".""t3_0""
limit 501";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);
            Assert.True(result.Rows.Count > 0);
            Assert.True(result.Columns.Contains("OrderedAt"));
            Assert.True(result.Columns.Contains("RequireAt"));

            var orderedAt = result.Rows[0]["OrderedAt"];
            var requireAt = result.Rows[0]["RequireAt"];
            Assert.True(orderedAt != System.DBNull.Value || requireAt != System.DBNull.Value);
        }
    }

    [Fact]
    public async Task DirectQuery_distinct_list_wrapper_order_by_null_helper_columns_desc_should_work_end_to_end()
    {
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select ""_"".""OrderedAt"" as ""c6"",
    ""_"".""RequireAt"" as ""c7""
from
(
    select ""OrderedAt"",
        ""RequireAt"",
        ""_"".""t0_0"" as ""t0_0"",
        ""_"".""t1_0"" as ""t1_0"",
        ""_"".""t2_0"" as ""t2_0"",
        ""_"".""t3_0"" as ""t3_0""
    from
    (
        select ""_"".""OrderedAt"",
            ""_"".""RequireAt"",
            ""_"".""o0"",
            ""_"".""o1"",
            ""_"".""t0_0"",
            ""_"".""t1_0"",
            ""_"".""t2_0"",
            ""_"".""t3_0""
        from
        (
            select ""_"".""OrderedAt"" as ""OrderedAt"",
                ""_"".""RequireAt"" as ""RequireAt"",
                ""_"".""o0"" as ""o0"",
                ""_"".""o1"" as ""o1"",
                case
                    when ""_"".""o0"" is not null
                    then ""_"".""o0""
                    else timestamp '1899-12-28 00:00:00'
                end as ""t0_0"",
                case
                    when ""_"".""o0"" is null
                    then 0
                    else 1
                end as ""t1_0"",
                case
                    when ""_"".""o1"" is not null
                    then ""_"".""o1""
                    else timestamp '1899-12-28 00:00:00'
                end as ""t2_0"",
                case
                    when ""_"".""o1"" is null
                    then 0
                    else 1
                end as ""t3_0""
            from
            (
                select ""rows"".""OrderedAt"" as ""OrderedAt"",
                    ""rows"".""RequireAt"" as ""RequireAt"",
                    ""rows"".""o0"" as ""o0"",
                    ""rows"".""o1"" as ""o1""
                from
                (
                    select ""OrderedAt"" as ""OrderedAt"",
                        ""RequireAt"" as ""RequireAt"",
                        ""RequireAt"" as ""o0"",
                        ""OrderedAt"" as ""o1""
                    from
                    (
                        from Orders as o
                        where o.Company = ""Companies/1-A"" OR o.Company = ""Companies/2-A""
                    ) ""$Table""
                ) ""rows""
                group by ""OrderedAt"",
                    ""RequireAt"",
                    ""o0"",
                    ""o1""
            ) ""_""
        ) ""_""
    ) ""_""
) ""_""
order by ""_"".""t0_0"" desc,
        ""_"".""t1_0"" desc,
        ""_"".""t2_0"" desc,
        ""_"".""t3_0"" desc
limit 501";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);
            Assert.True(result.Rows.Count > 0);
            Assert.True(result.Columns.Contains("OrderedAt"));
            Assert.True(result.Columns.Contains("RequireAt"));
        }
    }

    [Fact]
    public async Task DirectQuery_distinct_list_wrapper_order_by_null_helper_columns_from_projected_inner_rql_should_work_end_to_end()
    {
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select ""_"".""OrderedAt"" as ""c2"",
    ""_"".""RequireAt"" as ""c3""
from
(
    select ""OrderedAt"",
        ""RequireAt"",
        ""_"".""t0_0"" as ""t0_0"",
        ""_"".""t1_0"" as ""t1_0"",
        ""_"".""t2_0"" as ""t2_0"",
        ""_"".""t3_0"" as ""t3_0""
    from
    (
        select ""_"".""OrderedAt"",
            ""_"".""RequireAt"",
            ""_"".""o0"",
            ""_"".""o1"",
            ""_"".""t0_0"",
            ""_"".""t1_0"",
            ""_"".""t2_0"",
            ""_"".""t3_0""
        from
        (
            select ""_"".""OrderedAt"" as ""OrderedAt"",
                ""_"".""RequireAt"" as ""RequireAt"",
                ""_"".""o0"" as ""o0"",
                ""_"".""o1"" as ""o1"",
                case
                    when ""_"".""o0"" is not null
                    then ""_"".""o0""
                    else timestamp '1899-12-28 00:00:00'
                end as ""t0_0"",
                case
                    when ""_"".""o0"" is null
                    then 0
                    else 1
                end as ""t1_0"",
                case
                    when ""_"".""o1"" is not null
                    then ""_"".""o1""
                    else timestamp '1899-12-28 00:00:00'
                end as ""t2_0"",
                case
                    when ""_"".""o1"" is null
                    then 0
                    else 1
                end as ""t3_0""
            from
            (
                select ""rows"".""OrderedAt"" as ""OrderedAt"",
                    ""rows"".""RequireAt"" as ""RequireAt"",
                    ""rows"".""o0"" as ""o0"",
                    ""rows"".""o1"" as ""o1""
                from
                (
                    select ""OrderedAt"" as ""OrderedAt"",
                        ""RequireAt"" as ""RequireAt"",
                        ""OrderedAt"" as ""o0"",
                        ""RequireAt"" as ""o1""
                    from
                    (
                        from Orders as o
                        where o.Company = ""Companies/1-A"" OR o.Company = ""Companies/2-A""
                        select { OrderedAt: o.OrderedAt, RequireAt: o.RequireAt}
                    ) ""$Table""
                ) ""rows""
                group by ""OrderedAt"",
                    ""RequireAt"",
                    ""o0"",
                    ""o1""
            ) ""_""
        ) ""_""
    ) ""_""
) ""_""
order by ""_"".""t0_0"",
        ""_"".""t1_0"",
        ""_"".""t2_0"",
        ""_"".""t3_0""
limit 501";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);
            Assert.True(result.Rows.Count > 0);
            Assert.True(result.Columns.Contains("OrderedAt"));
            Assert.True(result.Columns.Contains("RequireAt"));
        }
    }

    [Fact]
    public async Task DirectQuery_select_id_should_work_end_to_end()
    {
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select ""_"".""id()""
from
(
    select ""rows"".""id()"" as ""id()""
    from
    (
        select ""id()""
        from
        (
            from Employees as e
            where id() in ('employees/1-A')
            select { id(): id(e) }
        ) ""$Table""
    ) ""rows""
    group by ""id()""
) ""_""
order by ""_"".""id()""
limit 501";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);
            Assert.Equal(1, result.Rows.Count);
            Assert.True(result.Columns.Contains("id()"));

            var id = result.Rows[0]["id()"]?.ToString();
            Assert.Equal("employees/1-A", id);
        }
    }

    [Fact]
    public async Task DirectQuery_select_json_should_work_end_to_end()
    {
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select ""_"".""json()""
from
(
    select ""rows"".""json()"" as ""json()""
    from
    (
        select ""json()""
        from
        (
            from Employees as e
            where id() in ('employees/1-A')
            select { ""json()"": e }
        ) ""$Table""
    ) ""rows""
    group by ""json()""
) ""_""
order by ""_"".""json()""
limit 501";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);
            Assert.Equal(1, result.Rows.Count);
            Assert.True(result.Columns.Contains("json()"));

            var json = result.Rows[0]["json()"]?.ToString();
            Assert.NotNull(json);
        }
    }

    [Fact]
    public async Task DirectQuery_grouped_sum_wrapper_from_desktop_should_work_end_to_end()
    {
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

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

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);
            Assert.True(result.Rows.Count > 0);
            Assert.True(result.Columns.Contains("Company"));
            Assert.True(result.Columns.Contains("a0"));
        }
    }

    [Fact]
    public async Task DirectQuery_desktop_grouped_sum_two_group_fields_with_outer_where_not_null_should_work_end_to_end()
    {

        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select ""_"".""Employee"",
    ""_"".""RequireAt"",
    ""_"".""a0""
from 
(
    select ""rows"".""Employee"" as ""Employee"",
        ""rows"".""RequireAt"" as ""RequireAt"",
        sum(""rows"".""Freight"") as ""a0""
    from 
    (
        from Orders
        where Company in ('Companies/1-A', 'Companies/2-A', 'Companies/3-A')
    ) ""rows""
    group by ""Employee"",
        ""RequireAt""
) ""_""
where not ""_"".""a0"" is null
limit 1000001";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);

            Assert.Equal(3, result.Columns.Count);
            Assert.True(result.Columns.Contains("Employee"));
            Assert.True(result.Columns.Contains("RequireAt"));
            Assert.True(result.Columns.Contains("a0"));
        }
    }

    [Fact(Skip = "Aggregate-only DirectQuery wrapper family not supported yet.")]
    public async Task DirectQuery_aggregate_only_sum_should_work_end_to_end()
    {
        using (var store = GetDocumentStore())
        {
            await store.Maintenance.SendAsync(new CreateSampleDataOperation());

            const string sql = @"select sum(""rows"".""PricePerUnit"") as ""a0""
from
(
    select ""$Table"".""PricePerUnit"" as ""PricePerUnit""
    from
    (
        from Products as p
        where id() in ('products/1-A','products/2-A')
        select { PricePerUnit: p.PricePerUnit }
    ) ""$Table""
) ""rows""";

            var result = await Act(store, sql);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Rows);
            Assert.Equal(1, result.Rows.Count);
            Assert.True(result.Columns.Contains("a0"));

            var raw = result.Rows[0]["a0"];
            Assert.NotNull(raw);
            Assert.NotEqual(System.DBNull.Value, raw);
            Assert.True(decimal.TryParse(raw.ToString(), out var sum));
            Assert.True(sum > 0);
        }
    }
}
#endif
