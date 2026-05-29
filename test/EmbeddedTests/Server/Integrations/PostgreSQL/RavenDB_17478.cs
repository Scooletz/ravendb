#if RUN_NPGSQL_TESTS
using System;
using System.Threading.Tasks;
using Npgsql;
using Xunit;

namespace EmbeddedTests.Server.Integrations.PostgreSQL
{
    public class RavenDB_17478 : PostgreSqlIntegrationTestBase
    {
        [Fact]
        public async Task ShouldRejectUnsupportedRqlQuery_WithUnhandledQueryError()
        {
            const string query =
                @"declare function name(e) {
                    if (!e)
                        return null;
                    return e.FirstName + "" "" + e.LastName;
                }
                from Employees as e
                where id() in ('employees/2-A', 'employees/1-A'  )
                load e.ReportsTo as boss
                select { Name: name(e), Manager: name(boss) }";

            using (var store = GetDocumentStore())
            {
                var pgException = await Assert.ThrowsAsync<PostgresException>(async () => await Act(store, query));

                Assert.Equal("54001", pgException.SqlState);
                Assert.StartsWith($"Unhandled query:{Environment.NewLine}", pgException.MessageText);
                Assert.Contains("declare function name", pgException.MessageText);
            }
        }
    }
}
#endif
