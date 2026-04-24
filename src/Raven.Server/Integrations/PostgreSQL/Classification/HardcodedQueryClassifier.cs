using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    internal static class HardcodedQueryClassifier
    {
        public static bool TryClassify(string queryText, out PgTable response)
        {
            response = null;

            if (SelectStmtShape.TryParseSelectStatements(queryText, out var selects) == false)
                return false;

            if (selects.Count == 1 && PowerBIQueryClassifier.TryClassify(selects[0], out response))
                return true;

            return NpgsqlQueryClassifier.TryClassify(selects, out response);
        }
    }
}
