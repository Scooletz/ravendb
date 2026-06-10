using PgSqlParser;

namespace Raven.Server.Integrations.PostgreSQL
{
    // Single-entry per-thread cache around pgsqlparser's Parser.Parse. PgQuery.CreateInstance
    // dispatches a SQL query through several arms (PowerBI / virtual interpreter / SQL to RQL
    // translator / unhandled diagnoser), each of which parses the same text independently. For
    // a query that falls through to the diagnoser the same string gets parsed 4–6 times.
    //
    // The cache is keyed by reference equality on queryText. CreateInstance is synchronous so
    // a per-thread sticky last-result is safe: the dispatch chain stays on one thread, the
    // same `queryText` string instance is threaded through every arm, and once the next query
    // arrives it overwrites the slot — no cross-query leakage and no unbounded memory.
    //
    // Result<ParseResult?> is a value-type wrapper around a managed payload (no native handles,
    // no IDisposable), so sharing the cached value across call sites within the same dispatch
    // is safe — every caller treats it as read-only.
    internal static class SqlAstCache
    {
        [System.ThreadStatic]
        private static string _lastText;

        [System.ThreadStatic]
        private static Result<ParseResult> _lastResult;

        [System.ThreadStatic]
        private static bool _hasCached;

        public static Result<ParseResult> GetOrParse(string queryText)
        {
            if (queryText != null && _hasCached && ReferenceEquals(_lastText, queryText))
                return _lastResult;

            var result = Parser.Parse(queryText);
            _lastText = queryText;
            _lastResult = result;
            _hasCached = true;
            return result;
        }
    }
}
