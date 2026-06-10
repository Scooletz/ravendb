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

        // PowerBI's standard wrap shape: `SELECT * FROM (USER_SQL) "_" LIMIT N`. When USER_SQL
        // contains a JOIN, the outer FromClause is a RangeSubselect — the JoinExpr lives one
        // level deep. Diagnoser must descend into RangeSubselect.Subquery to find it; otherwise
        // PowerBI users see the generic `Unhandled query` SQL dump instead of the actionable
        // load/include hint.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Join_InsidePowerBiWrapper_IsDetected()
        {
            var sql = """
                SELECT *
                FROM (
                    SELECT o."Company", e."LastName"
                    FROM "public"."Orders" o
                    JOIN "public"."Employees" e ON o."Employee" = e."id"
                ) "_"
                LIMIT 1000001
                """;

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(sql, out var message));
            Assert.Contains("JOIN", message);
            Assert.Contains("load", message);
        }

        // Doubly-wrapped shape that some PowerBI variants emit — JOIN two levels deep.
        // Bounded recursion must still find it.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Join_InsideNestedWrapper_IsDetected()
        {
            var sql = """
                SELECT *
                FROM (
                    SELECT *
                    FROM (
                        SELECT a, b
                        FROM "public"."T1"
                        JOIN "public"."T2" ON T1.id = T2.id
                    ) "inner"
                ) "outer"
                """;

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(sql, out _));
        }

        // Negative pin: a wrapper that doesn't contain a JOIN inside must NOT be classified as
        // JOIN — the diagnoser would otherwise misclassify every PowerBI-wrapped query.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Wrapper_WithoutJoinInside_IsNotClassifiedAsJoin()
        {
            var sql = """
                SELECT "Company", "Freight"
                FROM (SELECT "Company", "Freight" FROM "public"."Orders") "_"
                LIMIT 100
                """;

            // No JOIN anywhere, no scalar aggregate, no min/max → diagnoser returns false.
            Assert.False(UnhandledQueryDiagnoser.TryDiagnose(sql, out _));
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

        // PowerBI's PostgreSQL connector splits the M `Query=` value on `;` client-side, so an
        // RQL `declare function {...; ...}` arrives as just the first piece — unbalanced braces,
        // unparseable. Diagnoser must catch this and point at the ASI workaround instead of
        // dumping the fragment with a generic "Unhandled query".
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void JsBodyFragment_FromPowerBiSemicolonSplit_PointsAtAsiWorkaround()
        {
            const string fragment = "declare function output(usage) { var r = usage.ModelLog.Response.filter(y => y.Id == usage.ModelId)";

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(fragment, out var message));
            Assert.Contains("fragment", message);
            Assert.Contains("declare function", message);
            Assert.Contains("semicolons", message);
        }

        // A complete `declare function { ... }` RQL with balanced braces should NOT be
        // classified — that's valid RQL and would dispatch through RqlQuery.TryParse normally.
        // Diagnoser only fires when both arms of the parse fail; this query parses as RQL.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CompleteDeclareFunction_NotClassifiedAsFragment()
        {
            const string complete = """
                declare function output(u) {
                    var r = u.ModelLog.Response.filter(y => y.Id == u.ModelId)
                    return { Id: u.ModelId, Response: r[0] }
                }

                from 'Usages' as x select output(x)
                """;

            // Braces are balanced here so the fragment-detector returns false; the parser
            // would also fail (this is RQL, not SQL) so other diagnoser arms also return false.
            // Final result: no classification — caller handles as plain "unsupported".
            Assert.False(UnhandledQueryDiagnoser.TryDiagnose(complete, out _));
        }

        // SQL queries without "declare function" anywhere must not get the fragment classification.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void PlainSql_NotClassifiedAsFragment()
        {
            Assert.False(UnhandledQueryDiagnoser.TryDiagnose("SELECT * FROM unknown_table", out _));
        }

        // min()/max() aggregates aren't supported by RavenDB's map-reduce engine — the
        // AggregationOperation enum models only Count and Sum. The diagnoser must catch this
        // (both with and without GROUP BY) and point at the ORDER BY + LIMIT 1 workaround.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void MinAggregate_WithGroupBy_Detected()
        {
            var sql = """
                SELECT "Company", min("Freight") AS "m"
                FROM "public"."Orders"
                GROUP BY "Company"
                """;

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(sql, out var message));
            Assert.Contains("min()", message);
            Assert.Contains("max()", message);
            Assert.Contains("ORDER BY", message);
            Assert.Contains("LIMIT 1", message);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void MaxAggregate_WithoutGroupBy_Detected()
        {
            // Bare scalar `SELECT max(x) FROM t` is doubly unsupported (no GROUP BY AND uses
            // max). The min/max diagnostic must win because its workaround is more useful.
            var sql = """SELECT max("Freight") FROM "public"."Orders" """;

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(sql, out var message));
            Assert.Contains("max()", message);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void MixedMinAndSum_Detected()
        {
            // If any projection is min/max we still produce the min/max-specific message —
            // explaining only "scalar aggregate without GROUP BY" would mislead since adding a
            // GROUP BY wouldn't help.
            var sql = """
                SELECT "Company", min("Freight"), sum("Freight")
                FROM "public"."Orders"
                GROUP BY "Company"
                """;

            Assert.True(UnhandledQueryDiagnoser.TryDiagnose(sql, out var message));
            Assert.Contains("min()", message);
        }
    }
}
