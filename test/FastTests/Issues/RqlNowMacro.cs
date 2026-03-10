using System;
using FastTests;
using Raven.Client;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Queries;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace FastTests.Issues
{
    public class RqlNowMacro : RavenTestBase
    {
        public RqlNowMacro(ITestOutputHelper output) : base(output)
        {
        }

        [RavenFact(RavenTestCategory.Querying)]
        public void Missing_now_parameter_uses_server_utc_time_when_query_parameters_are_null()
        {
            var now = DateTime.UtcNow;

            using (var store = GetDocumentStore())
            {
                StoreEmployees(store,
                    new Employee
                    {
                        Id = "employees/1-A",
                        HiredAt = now.AddDays(-1)
                    },
                    new Employee
                    {
                        Id = "employees/2-A",
                        HiredAt = now.AddDays(1)
                    });

                var count = ExecuteQuery(store, new IndexQuery
                {
                    Query = "from Employees where HiredAt <= $now",
                    WaitForNonStaleResults = true
                });

                Assert.Equal(1, count);
            }
        }

        [RavenFact(RavenTestCategory.Querying)]
        public void Missing_now_parameter_uses_server_utc_time_when_query_parameters_are_empty()
        {
            var now = DateTime.UtcNow;

            using (var store = GetDocumentStore())
            {
                StoreEmployees(store,
                    new Employee
                    {
                        Id = "employees/1-A",
                        HiredAt = now.AddDays(-1)
                    },
                    new Employee
                    {
                        Id = "employees/2-A",
                        HiredAt = now.AddDays(1)
                    });

                var count = ExecuteQuery(store, new IndexQuery
                {
                    Query = "from Employees where HiredAt <= $now",
                    QueryParameters = new Parameters(),
                    WaitForNonStaleResults = true
                });

                Assert.Equal(1, count);
            }
        }

        [RavenFact(RavenTestCategory.Querying)]
        public void Explicit_now_parameter_value_is_preserved()
        {
            var now = DateTime.UtcNow;

            using (var store = GetDocumentStore())
            {
                StoreEmployees(store,
                    new Employee
                    {
                        Id = "employees/1-A",
                        HiredAt = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new Employee
                    {
                        Id = "employees/2-A",
                        HiredAt = now.AddMinutes(-1)
                    });

                var count = ExecuteQuery(store, new IndexQuery
                {
                    Query = "from Employees where HiredAt >= $now",
                    QueryParameters = new Parameters
                    {
                        ["now"] = "1994-04-01T00:00:00.0000000"
                    },
                    WaitForNonStaleResults = true
                });

                Assert.Equal(1, count);
            }
        }

        private static void StoreEmployees(Raven.Client.Documents.IDocumentStore store, params Employee[] employees)
        {
            using (var session = store.OpenSession())
            {
                foreach (var employee in employees)
                    session.Store(employee);

                session.SaveChanges();
            }
        }

        private static int ExecuteQuery(Raven.Client.Documents.IDocumentStore store, IndexQuery query)
        {
            using (var commands = store.Commands())
            {
                var command = new QueryCommand(commands.Session, query);
                commands.RequestExecutor.Execute(command, commands.Context);
                return command.Result.Results.Length;
            }
        }

        private class Employee
        {
            public string Id { get; set; }

            public DateTime HiredAt { get; set; }
        }
    }
}
