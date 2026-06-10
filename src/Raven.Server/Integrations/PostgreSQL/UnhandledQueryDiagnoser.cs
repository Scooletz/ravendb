using System;
using PgSqlParser;

namespace Raven.Server.Integrations.PostgreSQL
{
    // Classifies SQL shapes the server can't yet execute, so the wire-level error tells the
    // client WHY rather than handing back a generic "Unhandled query: <full SQL dump>". Only
    // consulted AFTER every TryParse dispatch arm has returned false — never in the hot path.
    //
    // The shapes we detect here are not bugs, they're known limitations of RavenDB's PG bridge:
    //   - SQL JOIN over user collections has no RQL equivalent (RQL uses load/include, not joins).
    //   - Scalar aggregate without GROUP BY (e.g. `SELECT sum(x) FROM t`) has no clean RQL form;
    //     RQL aggregates require a group key. Wrap in GROUP BY or compute client-side.
    // Anything else still surfaces as the generic "Unhandled query" so we don't accidentally
    // misclassify a fixable bug as a known limitation.
    internal static class UnhandledQueryDiagnoser
    {
        public static bool TryDiagnose(string queryText, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            // PowerBI's PostgreSQL connector splits the M `Query=` value on `;` client-side
            // before sending. For RQL with a `declare function {...; ...}` body, the JS-body
            // semicolons cause it to send only the first fragment — text that begins with
            // `declare function` and has unbalanced `{`. The fragment fails to parse as either
            // SQL or RQL, and the generic "Unhandled query" message doesn't tell the user the
            // ASI workaround. Catch it first, before the parser-based checks, since the
            // fragment fails to parse.
            if (LooksLikeJsBodyFragment(queryText))
            {
                message = "The query content looks like a fragment of a `declare function {...}` body. Some PostgreSQL clients split queries on `;` before sending, so only the first piece reaches the server. Remove the semicolons from the JS body.";
                return true;
            }

            SelectStmt outer;
            try
            {
                var parseResult = SqlAstCache.GetOrParse(queryText);
                if (parseResult.IsSuccess == false || parseResult.Value?.Stmts is not { Count: 1 })
                    return false;
                outer = parseResult.Value.Stmts[0]?.Stmt?.SelectStmt;
            }
            catch
            {
                // Parser blew up on some shape we don't recognize at all — let the generic
                // catch-all message handle it.
                return false;
            }

            if (outer == null)
                return false;

            if (HasIntersectOrExcept(outer))
            {
                message = "SQL INTERSECT / EXCEPT are not supported. The PG-bridge virtual catalog implements UNION / UNION ALL only — express the intent with a different combination (e.g. UNION of conditions) or compute the result client-side.";
                return true;
            }

            if (HasJoinExpr(outer))
            {
                message = "SQL JOIN over RavenDB collections is not supported. RavenDB models cross-document relationships via document IDs rather than relational joins — express the relationship in RQL using `load` / `include`, or denormalize the data into the parent document.";
                return true;
            }

            if (HasMinOrMaxAggregate(outer))
            {
                message = "min() and max() aggregates are not supported by RavenDB's map-reduce engine — its AggregationOperation set is limited to Count and Sum. To get the minimum / maximum of a field, do `ORDER BY <field> ASC LIMIT 1` (min) or `ORDER BY <field> DESC LIMIT 1` (max) and read the single value client-side.";
                return true;
            }

            if (HasAvgAggregate(outer))
            {
                message = "avg() is not supported by RavenDB's map-reduce engine — its AggregationOperation set is limited to Count and Sum. Compute the average client-side: select sum(<field>) and count(*) and divide.";
                return true;
            }

            if (IsScalarAggregateWithoutGroupBy(outer))
            {
                message = "Scalar aggregate (e.g. `SELECT sum(...) FROM t` with no GROUP BY) is not yet supported. Wrap the query in a GROUP BY (even a constant key) or compute the aggregate client-side from the underlying rows.";
                return true;
            }

            return false;
        }

        // Detects a textual fragment of a `declare function {...}` body. The fragment will
        // have more open braces than close braces (because the client cut us off at a `;`
        // inside the body). Pure textual check — pgsqlparser fails on the fragment, so we
        // run this before any AST-based diagnostics. Two guards keep this from false-firing:
        //   1) The text must START with `declare function`. Stops a SQL query that happens
        //      to contain "declare function" inside a string literal from triggering.
        //   2) Brace counting skips quoted regions. Stops a JS string in the body like
        //      `return "}"` from balancing out a real open brace.
        // We deliberately do NOT skip `--` or `/* */` style comments: the fragment is JS, not
        // SQL, and `i--` (JS decrement) is far more common inside a function body than a
        // SQL line comment — skipping `--` would falsely consume real characters.
        private static bool LooksLikeJsBodyFragment(string queryText)
        {
            var trimmed = queryText.AsSpan().TrimStart();
            if (trimmed.StartsWith("declare function", System.StringComparison.OrdinalIgnoreCase) == false)
                return false;

            int openBraces = 0;
            int closeBraces = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                var ch = trimmed[i];

                // Single-quoted string: '' is an escaped quote.
                if (ch == '\'')
                {
                    i++;
                    while (i < trimmed.Length)
                    {
                        if (trimmed[i] == '\'')
                        {
                            if (i + 1 < trimmed.Length && trimmed[i + 1] == '\'')
                            {
                                i += 2;
                                continue;
                            }
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                // Double-quoted string / identifier. JS bodies use "..." for strings; skipping
                // its contents stops a stray `}` inside a JS string from balancing the body.
                if (ch == '"')
                {
                    i++;
                    while (i < trimmed.Length)
                    {
                        if (trimmed[i] == '"')
                        {
                            if (i + 1 < trimmed.Length && trimmed[i + 1] == '"')
                            {
                                i += 2;
                                continue;
                            }
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                if (ch == '{') openBraces++;
                else if (ch == '}') closeBraces++;
            }
            return openBraces > closeBraces;
        }

        // True if the outermost SelectStmt (or any arm at the same set-op level) uses INTERSECT
        // or EXCEPT. We deliberately don't descend into FROM-clause subselects here: only the
        // outer combination matters for the diagnostic.
        private static bool HasIntersectOrExcept(SelectStmt selectStmt)
        {
            if (selectStmt == null)
                return false;
            return selectStmt.Op == SetOperation.SetopIntersect
                || selectStmt.Op == SetOperation.SetopExcept;
        }

        // True if any SelectStmt in the tree has a JoinExpr in its FROM clause. Must descend
        // into RangeSubselect.Subquery (PowerBI's standard `select * from (USER_SQL) "_"` wrap
        // buries the user's JOIN one level deep) and into Larg/Rarg of set operations. Bounded
        // recursion guards against pathological nesting.
        private static bool HasJoinExpr(SelectStmt selectStmt) => HasJoinExpr(selectStmt, depth: 0);

        private const int MaxJoinSearchDepth = 32;

        private static bool HasJoinExpr(SelectStmt selectStmt, int depth)
        {
            if (selectStmt == null || depth >= MaxJoinSearchDepth)
                return false;

            // Recurse into UNION/INTERSECT/EXCEPT arms.
            if (selectStmt.Op != SetOperation.SetopNone)
            {
                if (HasJoinExpr(selectStmt.Larg, depth + 1)) return true;
                if (HasJoinExpr(selectStmt.Rarg, depth + 1)) return true;
            }

            if (selectStmt.FromClause == null)
                return false;

            foreach (var item in selectStmt.FromClause)
            {
                if (item == null)
                    continue;
                if (item.JoinExpr != null)
                    return true;
                if (item.RangeSubselect?.Subquery?.SelectStmt is { } inner
                    && HasJoinExpr(inner, depth + 1))
                    return true;
            }
            return false;
        }

        // True iff any target projection is a min() or max() FuncCall. We surface this BEFORE the
        // generic scalar-aggregate-without-GROUP-BY check because min/max are unsupported in BOTH
        // shapes (with and without GROUP BY) — RavenDB's AggregationOperation enum only models
        // Count and Sum — and the workaround (ORDER BY + LIMIT 1) is different from the
        // "wrap in GROUP BY" suggestion that fits sum/count/avg.
        private static bool HasMinOrMaxAggregate(SelectStmt selectStmt)
        {
            if (selectStmt.TargetList is not { Count: > 0 } targets)
                return false;

            foreach (var t in targets)
            {
                var funcCall = t?.ResTarget?.Val?.FuncCall;
                if (funcCall == null)
                    continue;

                var name = funcCall.Funcname is { Count: > 0 }
                    ? funcCall.Funcname[funcCall.Funcname.Count - 1]?.String?.Sval
                    : null;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (string.Equals(name, "min", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "max", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // True iff any target projection is an avg() FuncCall. avg has no RQL grouped-SELECT form
        // (RQL aggregates are Count and Sum only), so it's unsupported in both scalar and GROUP BY
        // shapes — surfaced before the generic scalar-aggregate check so the message names avg and
        // gives the sum/count workaround rather than the "wrap in GROUP BY" hint that won't help.
        private static bool HasAvgAggregate(SelectStmt selectStmt)
        {
            if (selectStmt.TargetList is not { Count: > 0 } targets)
                return false;

            foreach (var t in targets)
            {
                var funcCall = t?.ResTarget?.Val?.FuncCall;
                if (funcCall == null)
                    continue;

                var name = funcCall.Funcname is { Count: > 0 }
                    ? funcCall.Funcname[funcCall.Funcname.Count - 1]?.String?.Sval
                    : null;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (string.Equals(name, "avg", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // True iff every projection is an aggregate FuncCall (count/sum/avg/min/max) AND there's
        // no GROUP BY anchoring those aggregates to a key. Mixed shapes (one aggregate + one bare
        // column) are NOT classified here — that's a SQL error in real PG, and our existing
        // translator already rejects it with a clearer message.
        private static bool IsScalarAggregateWithoutGroupBy(SelectStmt selectStmt)
        {
            if (selectStmt.GroupClause is { Count: > 0 })
                return false;
            if (selectStmt.TargetList is not { Count: > 0 } targets)
                return false;

            foreach (var t in targets)
            {
                var funcCall = t?.ResTarget?.Val?.FuncCall;
                if (funcCall == null)
                    return false;

                var name = funcCall.Funcname is { Count: > 0 }
                    ? funcCall.Funcname[funcCall.Funcname.Count - 1]?.String?.Sval
                    : null;
                if (string.IsNullOrEmpty(name))
                    return false;

                if (IsAggregateFunctionName(name) == false)
                    return false;
            }
            return true;
        }

        private static bool IsAggregateFunctionName(string name)
        {
            // Match the SQL standard aggregate set; covers what PowerBI / pgAdmin actually emit.
            return string.Equals(name, "count", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "sum",   System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "avg",   System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "min",   System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "max",   System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
