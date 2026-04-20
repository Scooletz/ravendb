using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    /// <summary>
    /// Top-level façade used by <c>HardcodedQuery.TryParse</c>. Parses the SQL query once,
    /// dispatches to the per-source classifiers, and resolves the classified
    /// <see cref="MetadataIntent"/> to a canonical <see cref="PgTable"/> response.
    ///
    /// The classifiers (<see cref="PowerBIQueryClassifier"/>, <see cref="NpgsqlQueryClassifier"/>)
    /// receive already-parsed <see cref="PgSqlParser.SelectStmt"/> objects — no re-parsing occurs
    /// inside them when called through this façade.
    /// </summary>
    internal static class HardcodedQueryClassifier
    {
        /// <summary>
        /// Parses <paramref name="queryText"/> once, classifies its metadata intent (PowerBI or
        /// Npgsql), and returns the canonical <see cref="PgTable"/> response. Returns false when
        /// the input is unparseable, unclassified, or the intent has no response mapping.
        /// </summary>
        public static bool TryClassify(string queryText, out PgTable response)
        {
            response = null;

            if (AstFeatures.TryParseSelectStatements(queryText, out var selects) == false)
                return false;

            // PowerBI only ever sends single-statement queries; try it first for the common case.
            if (selects.Count == 1)
            {
                if (PowerBIQueryClassifier.TryClassify(selects[0], out var powerBiIntent))
                    return powerBiIntent.TryResolveToResponse(out response);
            }

            // Npgsql handles both single statements and the two-statement version+current_setting batch.
            if (NpgsqlQueryClassifier.TryClassify(selects, out var npgsqlIntent))
                return npgsqlIntent.TryResolveToResponse(out response);

            return false;
        }
    }
}
