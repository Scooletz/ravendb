using System;
using System.Globalization;
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
        private const string Employee1Id = "employees/1-A";
        private const string Employee2Id = "employees/2-A";
        private const string LessOrEqualNowQuery = "from Employees where HiredAt <= $now";
        private const string GreaterOrEqualNowQuery = "from Employees where HiredAt >= $now";
        private const string FarPastHiredAt = "2000-01-01T00:00:00.0000000Z";
        private const string FarFutureHiredAt = "2100-01-01T00:00:00.0000000Z";
        private const string BeforeExplicitNow = "1990-01-01T00:00:00.0000000Z";
        private const string ExplicitNow = "1994-04-01T00:00:00.0000000Z";
        private const string AfterExplicitNow = "2000-01-01T00:00:00.0000000Z";

        public RqlNowMacro(ITestOutputHelper output) : base(output)
        {
        }

        [RavenFact(RavenTestCategory.Querying)]
        public void Missing_now_parameter_uses_server_utc_time_when_query_parameters_are_null()
        {
            using (var store = GetDocumentStore())
            {
                StoreEmployees(store,
                    new Employee
                    {
                        Id = Employee1Id,
                        HiredAt = ParseUtc(FarPastHiredAt)
                    },
                    new Employee
                    {
                        Id = Employee2Id,
                        HiredAt = ParseUtc(FarFutureHiredAt)
                    });

                var count = ExecuteQuery(store, new IndexQuery
                {
                    Query = LessOrEqualNowQuery,
                    WaitForNonStaleResults = true
                });

                Assert.Equal(1, count);
            }
        }

        [RavenFact(RavenTestCategory.Querying)]
        public void Missing_now_parameter_uses_server_utc_time_when_query_parameters_are_empty()
        {
            using (var store = GetDocumentStore())
            {
                StoreEmployees(store,
                    new Employee
                    {
                        Id = Employee1Id,
                        HiredAt = ParseUtc(FarPastHiredAt)
                    },
                    new Employee
                    {
                        Id = Employee2Id,
                        HiredAt = ParseUtc(FarFutureHiredAt)
                    });

                var count = ExecuteQuery(store, new IndexQuery
                {
                    Query = LessOrEqualNowQuery,
                    QueryParameters = new Parameters(),
                    WaitForNonStaleResults = true
                });

                Assert.Equal(1, count);
            }
        }

        [RavenFact(RavenTestCategory.Querying)]
        public void Explicit_now_parameter_value_is_preserved()
        {
            using (var store = GetDocumentStore())
            {
                StoreEmployees(store,
                    new Employee
                    {
                        Id = Employee1Id,
                        HiredAt = ParseUtc(BeforeExplicitNow)
                    },
                    new Employee
                    {
                        Id = Employee2Id,
                        HiredAt = ParseUtc(AfterExplicitNow)
                    });

                var count = ExecuteQuery(store, new IndexQuery
                {
                    Query = GreaterOrEqualNowQuery,
                    QueryParameters = new Parameters
                    {
                        ["now"] = ExplicitNow
                    },
                    WaitForNonStaleResults = true
                });

                Assert.Equal(1, count);
            }
        }

        private static DateTime ParseUtc(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
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
