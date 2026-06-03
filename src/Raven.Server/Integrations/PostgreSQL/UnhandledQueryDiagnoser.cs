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

            SelectStmt outer;
            try
            {
                var parseResult = Parser.Parse(queryText);
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

            if (HasJoinExpr(outer))
            {
                message = "SQL JOIN over RavenDB collections is not supported. RavenDB models cross-document relationships via document IDs rather than relational joins — express the relationship in RQL using `load` / `include`, or denormalize the data into the parent document.";
                return true;
            }

            if (IsScalarAggregateWithoutGroupBy(outer))
            {
                message = "Scalar aggregate (e.g. `SELECT sum(...) FROM t` with no GROUP BY) is not yet supported. Wrap the query in a GROUP BY (even a constant key) or compute the aggregate client-side from the underlying rows.";
                return true;
            }

            return false;
        }

        private static bool HasJoinExpr(SelectStmt selectStmt)
        {
            if (selectStmt.FromClause == null)
                return false;
            foreach (var item in selectStmt.FromClause)
            {
                if (item?.JoinExpr != null)
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
