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

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void QuotedExplicitProjectionWithLimit_EmitsLimitClause()
        {
            // Regression for the pgAdmin "LIMIT 5 returns all 830 rows" bug. The translator was
            // never the problem — it correctly emits `limit 0, 5` here — but earlier debug logs
            // proved the LIMIT was being silently dropped downstream. The downstream fix lives in
            // RqlQuery.RunRqlQuery (it now applies Metadata.Query.Limit to PageSize when no
            // explicit _limit override is set). This test pins the translator side so any future
            // refactor that loses the LIMIT projection regresses loudly here too.
            var sql = """
                SELECT "Company", "Freight" AS "shipping_cost", "OrderedAt" AS "placed"
                FROM "public"."Orders"
                LIMIT 5
                """;
            var expected = "from 'Orders' select Company, Freight as shipping_cost, OrderedAt as placed limit 0, 5";
            Assert.Equal(expected, Translate(sql));
        }

        // Gap #3: NOT IN. RavenDB's client query API has no WhereNotIn — instead callers flip the
        // polarity via NegateNext() applied to the WhereIn that follows. The translator used to
        // throw `NOT IN is not supported`; now it threads negation through correctly. NegateNext()
        // also prepends an `exists(<field>)` clause so the negation is null-safe (RQL: rows where
        // the field is missing don't match a negated predicate; PG's NOT IN semantics match).
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void NotIn_FlipsViaNegateNext()
        {
            var sql = @"SELECT ""Company"" FROM ""public"".""Orders"" WHERE ""Freight"" NOT IN (1.21, 1.35) LIMIT 5";
            var expected = "from 'Orders' where exists(Freight) and not Freight in (1.21, 1.35) select Company limit 0, 5";

            Assert.Equal(expected, Translate(sql));
        }

        // Gap #3 (continued): general NOT around a primitive predicate. NegateNext() flips the
        // next emitted predicate; compound NOTs (e.g. NOT(a AND b)) still throw — see the
        // explicit ParsedNot guard in PgSqlToRqlTranslator.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void NotBinary_FlipsViaNegateNext()
        {
            var sql = @"SELECT ""Company"" FROM ""public"".""Orders"" WHERE NOT (""Freight"" > 50) LIMIT 5";
            var expected = "from 'Orders' where exists(Freight) and not Freight > 50 select Company limit 0, 5";

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

        // The PG endpoint exposes the document identifier as `id` (the PG-idiomatic surface
        // name — see PgSyntheticColumns); under the hood it's still RQL's `id()` function.
        // The translator maps both `id` and `id()` references in user SQL to `id()` in RQL
        // so the engine reads the document identifier instead of looking for a stored field
        // literally called `id`.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_21_SelectColumns()
        {
            var sql = "SELECT id, name FROM users";
            var expected = "from 'users' select id(), name";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_22_SelectColumnsWithWhere()
        {
            var sql = "SELECT id, status, shipTo.city FROM orders WHERE amount > 10";
            var expected = "from 'orders' where amount > 10 select id(), status, shipto.city";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Complex_23_CountStar()
        {
            var sql = "SELECT COUNT(*) FROM orders";
            var expected = "from 'orders' select count()";

            Assert.Equal(expected, Translate(sql));
        }

        // PowerBI's row-preview / drill-down queries decorate their projection list with
        // constant markers (e.g. `1 as "c0"`) so the client can count back a fixed shape.
        // The translator has to forward the literal — silently dropping the column produces
        // a `Field count mismatch when mapping column types. N vs N-1` error PowerBI-side.
        // The SQL alias must survive too so PowerBI's column-name lookup succeeds.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void ConstLiteral_IntegerProjection_WithAlias_PreservesLiteralAndAlias()
        {
            var sql = "SELECT name, 1 AS \"c0\" FROM users";
            var expected = "from 'users' select name, 1 as c0";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void ConstLiteral_StringProjection_WithAlias_QuotesValueAndPreservesAlias()
        {
            var sql = "SELECT name, 'literal' AS \"marker\" FROM users";
            var expected = "from 'users' select name, 'literal' as marker";

            Assert.Equal(expected, Translate(sql));
        }

        // Single-quote inside a string literal must double up — RQL uses the same escape
        // convention as SQL standards (and PG itself). Without this, e.g.
        // `'O''Brien' as note` would break RQL parsing of the projection.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void ConstLiteral_StringWithSingleQuote_EscapesByDoubling()
        {
            var sql = "SELECT name, 'O''Brien' AS \"note\" FROM users";
            var expected = "from 'users' select name, 'O''Brien' as note";

            Assert.Equal(expected, Translate(sql));
        }

        // RQL's scanner treats backslash as an escape character inside single-quoted strings, so a
        // backslash in a SQL string value must be doubled when emitted as an RQL literal. Without
        // this, `WHERE name = 'a\b'` emits `'a\b'`, which RQL decodes as `a` + backspace (silent
        // value corruption), and a crafted value can terminate the literal early and inject RQL
        // once the emitted query is re-parsed.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void WhereStringWithBackslash_EscapesBackslashByDoubling()
        {
            var sql = "SELECT * FROM users WHERE name = 'a\\b'";
            var expected = "from 'users' where name = 'a\\\\b'";

            Assert.Equal(expected, Translate(sql));
        }

        // Same backslash-escaping requirement on the const-projection path (TryRenderRqlLiteral),
        // which now shares the WHERE translator's QuoteString helper.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void ConstLiteral_StringWithBackslash_EscapesByDoubling()
        {
            var sql = "SELECT name, 'a\\b' AS \"note\" FROM users";
            var expected = "from 'users' select name, 'a\\\\b' as note";

            Assert.Equal(expected, Translate(sql));
        }

        // Regression: ORDER BY on the grouping key when that key is not in the SELECT list
        // (`SELECT sum(Freight) ... GROUP BY Company ORDER BY Company`) must fall through cleanly —
        // TryParse returns false — rather than throwing InvalidOperationException from a First()
        // with no matching projection, which previously escaped TryParse's catch as an unhandled error.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void GroupByOrderByOnNonProjectedKey_FailsGracefullyWithoutThrowing()
        {
            var sql = "SELECT sum(Freight) FROM Orders GROUP BY Company ORDER BY Company";

            var translated = Raven.Server.Integrations.PostgreSQL.Translation.PgSqlToRqlTranslator.TryParse(sql, Array.Empty<int>(), out _);

            Assert.False(translated);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void ConstLiteral_BooleanProjection_PreservesAsRqlBoolean()
        {
            var sql = "SELECT name, true AS \"flag\" FROM users";
            var expected = "from 'users' select name, true as flag";

            Assert.Equal(expected, Translate(sql));
        }

        // A literal without an explicit AS alias falls through to the field expression itself,
        // matching the existing single-arg SelectFields semantics. RQL accepts `select 1`
        // (auto-naming the column in the result).
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void ConstLiteral_IntegerProjection_WithoutAlias_OmitsAsClause()
        {
            var sql = "SELECT name, 1 FROM users";
            var expected = "from 'users' select name, 1";

            Assert.Equal(expected, Translate(sql));
        }

        // PowerBI's Top-N visual-level filter on a Clustered Bar Chart fires this exact shape
        // (alias-qualified projection + alias-qualified count argument). Before the fromAlias
        // fix in BuildProjectionForGroupByTarget / BuildCountProjection, ExtractFieldName
        // returned `"rows.Freight"` for the projection and `"rows.Freight"` for the count
        // argument, neither matching the GROUP BY key `"Freight"` (stripped via fromAlias),
        // so the translator threw `UnsupportedGroupByMessage` and the query fell through to
        // the factory's `Unhandled query` error.
        // PowerBI always emits an `AS` alias on aggregate projections (`as "a0"`, `as "a1"`...).
        // RQL's implicit alias for `count(Freight)` is `Freight`, identical to a sibling
        // group-by-key projection of `Freight`, and RQL rejects that with
        // `Duplicate alias 'Freight' detected`. The translator must preserve the SQL alias so
        // the RQL becomes `count(Freight) as a0` and the implicit-alias collision is avoided.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void GroupBy_WithFromAlias_QualifiedProjectionAndAggregateArg_StripsAliasAndPreservesAggregateAlias()
        {
            var sql = """
                select "rows"."Freight" as "Freight", count("rows"."Freight") as "a0"
                from "public"."Orders" "rows"
                group by "Freight"
                limit 1000001
                """;
            var expected = "from 'Orders' group by Freight select Freight, count(Freight) as a0 limit 0, 1000001";

            Assert.Equal(expected, Translate(sql));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void GroupBy_WithFromAlias_SumWithAliasQualifiedArg_PreservesAggregateAlias()
        {
            var sql = """
                select "rows"."Company" as "Company", sum("rows"."Freight") as "a0"
                from "public"."Orders" "rows"
                group by "Company"
                """;
            var expected = "from 'Orders' group by Company select Company, sum(Freight) as a0";

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

        // PowerBI's distinct-values probe — used when populating slicer / filter-dropdown options.
        // Instead of `SELECT DISTINCT col1, col2 ...` it sends `SELECT col1, col2 ... GROUP BY col1, col2`.
        // For multi-column shapes we emit `group by ... select ...` rather than `select distinct
        // ...` because RQL's `select distinct` isn't a tuple-distinct (it dedupes by first-field
        // semantics and leaves duplicate tuples behind, which breaks PowerBI's mashup engine
        // with a SubstituteWithIndex match-count error).
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void GroupBy_WithoutAggregates_TwoColumns_TranslatesToGroupByDistinct()
        {
            var sql = "SELECT status, region FROM orders GROUP BY status, region";
            var expected = "from 'orders' group by status, region select status, region";
            Assert.Equal(expected, Translate(sql));
        }

        // The exact shape PowerBI Desktop fires for a two-column slicer / dropdown probe against
        // a `public.X` table — wrapper alias on the source, both columns aliased in the SELECT,
        // and PowerBI's 1,000,001-row sentinel limit. Pinned so the recognizer-side dispatch
        // (PowerBIFetchQuery → PgSqlToRqlTranslator) keeps working end-to-end.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void GroupBy_PowerBI_DistinctValuesProbe_AliasedSource_TranslatesToGroupBy()
        {
            var sql = """
                select "rows"."Company" as "Company", "rows"."Freight" as "Freight"
                from "public"."Orders" "rows"
                group by "Company", "Freight"
                limit 1000001
                """;

            var rql = Translate(sql);

            Assert.Contains("from 'Orders'", rql, StringComparison.Ordinal);
            Assert.Contains("group by Company, Freight", rql, StringComparison.Ordinal);
            Assert.Contains("select Company, Freight", rql, StringComparison.Ordinal);
            Assert.Contains("limit 0, 1000001", rql, StringComparison.OrdinalIgnoreCase);
        }

        // Existing single-column-without-aggregate case must keep working — same path that
        // SELECT DISTINCT goes through, just via GROUP BY surface. Regression pin for the
        // single-col branch when ApplyGroupBy was rewritten to handle multi-col.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void GroupBy_WithoutAggregates_SingleColumn_TranslatesToDistinct()
        {
            var sql = "SELECT status FROM orders GROUP BY status";
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

        // The shape PowerBI actually sends for incremental refresh. A $N placeholder in a WHERE
        // value can't be inlined at translate time (Parse precedes Bind in the Extended Query
        // Protocol), so the translator emits an RQL parameter reference instead. The 1-based PG
        // index maps straight through: SQL $1/$2 → RQL $1/$2, which PgQuery.Bind then fills in.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void DateRange_ParameterizedBounds_TranslatesWithParamRefs()
        {
            var sql = """SELECT * FROM "Orders" WHERE "OrderedAt" >= $1 AND "OrderedAt" < $2""";
            var rql = Translate(sql);

            Assert.Contains("from 'Orders'", rql, StringComparison.Ordinal);
            Assert.Contains("OrderedAt", rql, StringComparison.Ordinal);
            // The PG parameter index doubles as the RQL parameter name (RQL allows numeric
            // names), so the placeholders survive translation as $1 / $2 rather than being
            // inlined as literals we don't have yet.
            Assert.Contains("$1", rql, StringComparison.Ordinal);
            Assert.Contains("$2", rql, StringComparison.Ordinal);
        }
    }
}
