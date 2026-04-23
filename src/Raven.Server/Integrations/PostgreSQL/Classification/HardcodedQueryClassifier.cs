using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    /// <summary>
    /// Top-level façade used by <c>HardcodedQuery.TryParse</c>. Parses the query once and
    /// dispatches to the per-source classifiers (<see cref="PowerBIQueryClassifier"/>,
    /// <see cref="NpgsqlQueryClassifier"/>), which return the canonical <see cref="PgTable"/>
    /// response directly. Classifiers are called with already-parsed ASTs — no re-parsing
    /// happens in the dispatch path.
    /// </summary>
    internal static class HardcodedQueryClassifier
    {
        public static bool TryClassify(string queryText, out PgTable response)
        {
            response = null;

            if (SelectStmtShape.TryParseSelectStatements(queryText, out var selects) == false)
                return false;

            // PowerBI only sends single-statement queries, so the common case goes first.
            if (selects.Count == 1 && PowerBIQueryClassifier.TryClassify(selects[0], out response))
                return true;

            // Npgsql handles both single statements and the two-statement version+current_setting batch.
            return NpgsqlQueryClassifier.TryClassify(selects, out response);
        }
    }
}
