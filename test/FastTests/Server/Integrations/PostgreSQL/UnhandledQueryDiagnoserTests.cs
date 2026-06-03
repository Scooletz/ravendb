using Raven.Server.Integrations.PostgreSQL;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    // Pins the targeted "why is this unsupported" messages we surface for the two largest known
    // limitations (SQL JOIN over user collections; scalar aggregate without GROUP BY). The string
    // wording is part of the user-facing contract — generic message rewrites that lose the
    // workaround hint regress here loudly. Everything else falls through to the legacy
    // "Unhandled query" path and TryDiagnose returns false.
    public sealed class UnhandledQueryDiagnoserTests(ITestOutputHelper output) : NoDisposalNeeded(output)
    {
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Join_OverUserCollections_DetectedWithLoadIncludeHint()
        {
            var sql = """
                SELECT o."Company", e."LastName"
                FROM "public"."Orders" o
                JOIN "public"."Employees" e ON o."Employee" = e."id"
                LIMIT 5
                """;

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(sql, out var message));
            Assert.Contains("JOIN", message);
            Assert.Contains("load", message);
            Assert.Contains("include", message);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Join_LeftOuter_AlsoDetected()
        {
            var sql = """
                SELECT o."Company"
                FROM "public"."Orders" o
                LEFT OUTER JOIN "public"."Employees" e ON o."Employee" = e."id"
                """;

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(sql, out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void ScalarSum_WithoutGroupBy_DetectedWithGroupByOrClientSideHint()
        {
            var sql = """SELECT sum("Freight") FROM "public"."Orders" """;

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(sql, out var message));
            Assert.Contains("Scalar aggregate", message);
            Assert.Contains("GROUP BY", message);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void ScalarCount_WithoutGroupBy_AlsoDetected()
        {
            var sql = """SELECT count(*) FROM "public"."Orders" """;

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(sql, out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void ScalarAggregate_QualifiedFuncName_AlsoDetected()
        {
            // PowerBI sometimes emits qualified aggregate names like `pg_catalog.sum(...)`;
            // the diagnoser must match the last segment of the function-name path.
            var sql = """SELECT pg_catalog.sum("Freight") FROM "public"."Orders" """;

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(sql, out _));
        }

        // GROUP BY + aggregate IS supported (translator handles it); we must NOT classify it as
        // scalar-aggregate-unsupported.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void GroupedAggregate_IsNotClassifiedAsScalarAggregate()
        {
            var sql = """
                SELECT "Company", sum("Freight") AS total
                FROM "public"."Orders"
                GROUP BY "Company"
                """;

            Assert.False(UnhandledQueryDiagnoser.TryDiagnose(sql, out _));
        }

        // Plain SELECT with no aggregates and no joins must fall through to the generic path —
        // the diagnoser must NOT eat a query someone else might be able to handle. (In practice
        // the diagnoser only runs AFTER every TryParse arm has returned false, so this case
        // never reaches it in production — but the unit test pins the negative behavior anyway.)
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void PlainSelect_NoClassification()
        {
            var sql = """SELECT "Company", "Freight" FROM "public"."Orders" LIMIT 5""";

            Assert.False(UnhandledQueryDiagnoser.TryDiagnose(sql, out _));
        }

        // Mixed shapes (one bare column + one aggregate without GROUP BY) are a SQL error in
        // real PG. We don't classify them as scalar-aggregate — the translator emits its own
        // clearer "mixing aggregates and non-aggregated columns" error for these.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void MixedAggregateAndBareColumn_NoClassification()
        {
            var sql = """SELECT "Company", sum("Freight") FROM "public"."Orders" """;

            Assert.False(UnhandledQueryDiagnoser.TryDiagnose(sql, out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Unparseable_NoClassification()
        {
            Assert.False(UnhandledQueryDiagnoser.TryDiagnose("not valid sql at all $$$", out _));
        }
    }
}
