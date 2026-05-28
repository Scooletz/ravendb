using System;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL.Translation
{
    public sealed class PgSqlToRqlTranslatorTests(ITestOutputHelper output) : NoDisposalNeeded(output)
    {
        private static string Translate(string sql)
        {
            Assert.True(Raven.Server.Integrations.PostgreSQL.Translation.PgSqlToRqlTranslator.TryParse(sql, Array.Empty<int>(), out var rql));
            return rql;
        }

        // Easy (10)

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Easy_01_SelectAllFromUsers()
        {
            var sql = "SELECT * FROM users";
            var expected = "from 'users'";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Easy_02_WhereAmountGreaterThan()
        {
            var sql = "SELECT * FROM orders WHERE amount > 10";
            var expected = "from 'orders' where amount > 10";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Easy_03_WhereNameEqualsString()
        {
            var sql = "SELECT * FROM users WHERE name = 'ayende'";
            var expected = "from 'users' where name = 'ayende'";

            Assert.Equal(expected, Translate(sql));
        }

        // SqlWhereParser used to reject `''` (treated as falsy via string.IsNullOrEmpty), so
        // the entire WHERE translation collapsed for queries that want to filter for empty
        // strings — an extremely common idiom. The Sval-null guard alone is the right gate.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void WhereStringEqualsEmpty_PreservesEmptyLiteral()
        {
            var sql = "SELECT * FROM users WHERE name = ''";
            var expected = "from 'users' where name = ''";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Easy_04_WhereActiveTrue()
        {
            var sql = "SELECT * FROM users WHERE active = true";
            var expected = "from 'users' where active = true";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Easy_05_WhereStatusNotEqualsString()
        {
            var sql = "SELECT * FROM orders WHERE status <> 'Cancelled'";
            var expected = "from 'orders' where status != 'Cancelled'";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Easy_06_WhereAnd()
        {
            var sql = "SELECT * FROM orders WHERE status = 'Pending' AND amount > 10";
            var expected = "from 'orders' where status = 'Pending' and amount > 10";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Easy_07_WhereOr()
        {
            var sql = "SELECT * FROM orders WHERE status = 'Pending' OR status = 'Shipped'";
            var expected = "from 'orders' where status = 'Pending' or status = 'Shipped'";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Easy_08_OrderByDesc()
        {
            var sql = "SELECT * FROM users ORDER BY name DESC";
            var expected = "from 'users' order by name desc";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Easy_09_LimitOffset()
        {
            var sql = "SELECT * FROM users LIMIT 10 OFFSET 20";
            var expected = "from 'users' limit 20, 10";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Easy_10_OrderByDescLimit()
        {
            // Unquoted identifiers follow PostgreSQL semantics (folded to lowercase).
            // Users who need exact RavenDB field casing must quote the identifier.
            var sql = "SELECT * FROM orders ORDER BY createdAt LIMIT 5";
            var expected = "from 'orders' order by createdat limit 0, 5";

            Assert.Equal(expected, Translate(sql));
        }

        // Mid (10)

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Mid_11_WhereDottedPathEquals()
        {
            var sql = "SELECT * FROM orders WHERE ShipTo.City = 'London'";
            var expected = "from 'orders' where shipto.city = 'London'";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Mid_12_Between()
        {
            var sql = "SELECT * FROM orders WHERE amount BETWEEN 10 AND 20";
            var expected = "from 'orders' where amount between 10 and 20";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Mid_13_InList()
        {
            var sql = "SELECT * FROM orders WHERE status IN ('Pending','Shipped')";
            var expected = "from 'orders' where status in ('Pending', 'Shipped')";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Mid_14_InListOnDottedPath()
        {
            var sql = "SELECT * FROM orders WHERE shipTo.city IN ('London','Paris')";
            var expected = "from 'orders' where shipto.city in ('London', 'Paris')";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Mid_15_ParenthesesAndOr()
        {
            var sql = "SELECT * FROM orders WHERE (status = 'Pending' OR status = 'Shipped') AND amount > 10";
            var expected = "from 'orders' where (status = 'Pending' or status = 'Shipped') and amount > 10";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Mid_16_OrderByTwoFields()
        {
            var sql = "SELECT * FROM orders ORDER BY createdAt DESC, amount ASC";
            var expected = "from 'orders' order by createdat desc, amount";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Mid_17_OrderByLimit()
        {
            var sql = "SELECT * FROM users WHERE name <> 'oren' ORDER BY name LIMIT 20";
            var expected = "from 'users' where name != 'oren' order by name limit 0, 20";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Mid_18_AndWithDottedPath()
        {
            var sql = "SELECT * FROM orders WHERE status = 'Pending' AND shipTo.city = 'London'";
            var expected = "from 'orders' where status = 'Pending' and shipto.city = 'London'";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Mid_19_IsNull()
        {
            var sql = "SELECT * FROM orders WHERE shippedAt IS NULL";
            var expected = "from 'orders' where shippedat = null";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Mid_20_AndWithParentheses()
        {
            var sql = "SELECT * FROM users WHERE active = true AND (name = 'ayende' OR name = 'oren')";
            var expected = "from 'users' where active = true and (name = 'ayende' or name = 'oren')";

            Assert.Equal(expected, Translate(sql));
        }

        // Complex (10)

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_21_SelectColumns()
        {
            var sql = "SELECT id, name FROM users";
            var expected = "from 'users' select id, name";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_22_SelectColumnsWithWhere()
        {
            var sql = "SELECT id, status, shipTo.city FROM orders WHERE amount > 10";
            var expected = "from 'orders' where amount > 10 select id, status, shipto.city";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_23_CountStar()
        {
            var sql = "SELECT COUNT(*) FROM orders";
            var expected = "from 'orders' select count()";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_24_Sum()
        {
            var sql = "SELECT COUNT(*), SUM(amount), AVG(score) FROM orders";
            var expected = "from 'orders' select count(), sum(amount), avg(score)";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_25_AvgWithWhere()
        {
            var sql = "SELECT AVG(amount) FROM orders WHERE status = 'Paid'";
            var expected = "from 'orders' where status = 'Paid' select avg(amount)";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_26_GroupByCount()
        {
            var sql = "SELECT status, COUNT(*) FROM orders GROUP BY status";
            var expected = "from 'orders' group by status select status, count()";
            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_27_GroupByCountOrderByCountDesc()
        {
            var sql = "SELECT status, COUNT(*) FROM orders GROUP BY status ORDER BY COUNT(*) DESC";
            var expected = "from 'orders' group by status order by 'count()' desc select status, count()";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_28_Distinct()
        {
            var sql = "SELECT DISTINCT status FROM orders";
            var expected = "from 'orders' select distinct status";
            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_29_IndexQueryContains()
        {
            var sql = "SELECT * FROM indexes.\"Users/ByName\" WHERE name = 'oren'";
            var expected = "from index 'Users/ByName' where name = 'oren'";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_30_Join()
        {
            var sql = "SELECT * FROM users u JOIN orders o ON u.id = o.user_id";
            var expected = "from 'orders' as o load o.user_id as u select { o: o, u: u }";

            Assert.Equal(expected, Translate(sql));
        }

        // ── Identifier case handling ─────────────────────────────────────────────────
        // Unquoted identifiers follow PostgreSQL semantics: pgsqlparser folds them to
        // lowercase before the AST is built (SQL standard behaviour). Quoted identifiers
        // preserve case via Sval. Users who need exact RavenDB field casing must quote
        // the identifier in SQL. See libpg_query issue #59 for upstream background.

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void IdentifierCasing_QuotedIdentifier_PreservesCase()
        {
            var sql = "SELECT \"Company\" FROM orders WHERE \"Title\" = 'Manager'";
            var rql = Translate(sql);

            Assert.Contains("Company", rql, StringComparison.Ordinal);
            Assert.Contains("Title", rql, StringComparison.Ordinal);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void IdentifierCasing_UnquotedIdentifier_FoldedToLowercase()
        {
            var sql = "SELECT Company FROM orders WHERE Title = 'Manager'";
            var rql = Translate(sql);

            // Per PostgreSQL semantics: unquoted folded to lowercase.
            Assert.Contains("company", rql, StringComparison.Ordinal);
            Assert.DoesNotContain("Company", rql, StringComparison.Ordinal);
            Assert.Contains("title", rql, StringComparison.Ordinal);
            Assert.DoesNotContain("Title", rql, StringComparison.Ordinal);
        }

        // ── PowerBI incremental refresh / date-range filters ──────────────────────────
        //
        // PowerBI's incremental refresh translates the `RangeStart`/`RangeEnd` parameters
        // into SQL with a parameterized date-range predicate on a chosen DateTime column.
        // The end-to-end shape is `WHERE "col" >= $1 AND "col" < $2`, with the bounds bound
        // at Bind time via the Extended Query Protocol.
        //
        // We test three forms: inline `timestamp 'YYYY-MM-DD'` literals (PG-idiomatic),
        // `'...'::timestamp` cast literals (functionally identical, different AST node
        // arrangement), and the parameterized form. The parameterized form is the one
        // PowerBI actually sends — it is *not* supported today: SqlWhereParser's scalar
        // extractor only handles AConst / TypeCast literals, not ParamRef. Documenting
        // the gap here keeps the limitation discoverable.

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void DateRange_InlineTimestampLiteral_TranslatesBothBounds()
        {
            // `timestamp 'X'` is PG-idiomatic typed-literal syntax — emits TypeCast in the AST.
            var sql = """SELECT * FROM orders WHERE "OrderedAt" >= timestamp '1996-08-01' AND "OrderedAt" < timestamp '1996-09-01'""";
            var rql = Translate(sql);

            Assert.Contains("from 'orders'", rql, StringComparison.Ordinal);
            Assert.Contains("OrderedAt", rql, StringComparison.Ordinal);
            // Both ends of the date range must reach the emitted RQL — incremental refresh
            // depends on RavenDB's auto-index seeing both bounds (else it scans the whole
            // collection per partition).
            Assert.Contains("1996-08", rql, StringComparison.Ordinal);
            Assert.Contains("1996-09", rql, StringComparison.Ordinal);
            Assert.Contains(">=", rql, StringComparison.Ordinal);
            Assert.Contains("<", rql, StringComparison.Ordinal);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void DateRange_CastTimestampLiteral_TranslatesBothBounds()
        {
            // `'X'::timestamp` — same TypeCast, different ordering in source. Some tools
            // (older PG-protocol clients) prefer this form. Should be equivalent.
            var sql = """SELECT * FROM orders WHERE "OrderedAt" >= '1996-08-01'::timestamp AND "OrderedAt" < '1996-09-01'::timestamp""";
            var rql = Translate(sql);

            Assert.Contains("from 'orders'", rql, StringComparison.Ordinal);
            Assert.Contains("OrderedAt", rql, StringComparison.Ordinal);
            Assert.Contains("1996-08", rql, StringComparison.Ordinal);
            Assert.Contains("1996-09", rql, StringComparison.Ordinal);
        }

        // PowerBI emits this exact shape for incremental-refresh windows. The query has
        // a quoted column reference + half-open range. We assert TryParse returns true and
        // the date column appears in the emitted RQL — both ends.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void DateRange_QuotedColumnHalfOpenWindow_TranslatesToRangeFilter()
        {
            var sql = """
                SELECT "OrderedAt", "Freight" FROM "Orders"
                WHERE "OrderedAt" >= timestamp '2024-01-01' AND "OrderedAt" < timestamp '2024-02-01'
                ORDER BY "OrderedAt"
                """;
            var rql = Translate(sql);

            Assert.Contains("from 'Orders'", rql, StringComparison.Ordinal);
            Assert.Contains("OrderedAt", rql, StringComparison.Ordinal);
            Assert.Contains("Freight", rql, StringComparison.Ordinal);
            Assert.Contains("2024-01", rql, StringComparison.Ordinal);
            Assert.Contains("2024-02", rql, StringComparison.Ordinal);
        }

        // The shape PowerBI actually sends for incremental refresh. Currently NOT supported:
        // SqlWhereParser.TryExtractScalar only handles AConst / TypeCast literals — not
        // ParamRef. Tracked here so the translator's progress on ParamRef support has a
        // pinned test to flip from Skip → passing once the feature lands.
        [RavenFact(RavenTestCategory.PostgreSql, Skip = "ParamRef in WHERE values is not yet supported by the SQL→RQL translator. Tracked: SqlWhereParser.TryExtractScalar should accept ParamRef and emit RQL with an inlined or named parameter reference.")]
        public void DateRange_ParameterizedBounds_TranslatesWithParamRefs()
        {
            var sql = """SELECT * FROM "Orders" WHERE "OrderedAt" >= $1 AND "OrderedAt" < $2""";
            var rql = Translate(sql);

            Assert.Contains("from 'Orders'", rql, StringComparison.Ordinal);
            Assert.Contains("OrderedAt", rql, StringComparison.Ordinal);
            // When implemented, RQL would carry parameter placeholders (e.g. $p0/$p1)
            // that the Bind-time parameter values then fill in.
            Assert.Contains("$", rql, StringComparison.Ordinal);
        }

        // Negative pin: until ParamRef support lands, TryParse must return false rather than
        // silently dropping the predicate (which would produce a query returning the whole
        // collection — exactly the silent-data-loss bug we want to avoid).
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void DateRange_ParameterizedBounds_CurrentlyRejectsTranslation()
        {
            var sql = """SELECT * FROM "Orders" WHERE "OrderedAt" >= $1 AND "OrderedAt" < $2""";

            // ParamRef in WHERE value → SqlWhereParser fails → translator returns false.
            // Caller (PgQuery.CreateInstance) then raises "Unhandled query", which the PG
            // client surfaces as an error rather than an unfiltered result set.
            Assert.False(
                Raven.Server.Integrations.PostgreSQL.Translation.PgSqlToRqlTranslator
                    .TryParse(sql, Array.Empty<int>(), out _),
                "Parameterized WHERE bounds should be rejected until ParamRef support is added — silent fall-through would return the whole table.");
        }
    }
}
