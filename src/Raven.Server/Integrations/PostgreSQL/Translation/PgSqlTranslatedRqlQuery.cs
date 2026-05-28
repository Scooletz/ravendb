using Raven.Server.Documents;

namespace Raven.Server.Integrations.PostgreSQL.Translation
{
    // Used for SQL queries that PgSqlToRqlTranslator handled with an EXPLICIT projection — i.e.
    // the user wrote `SELECT col1, col2 FROM t`, not `SELECT * FROM t`. The base RqlQuery class
    // is designed around PowerBI's needs and unconditionally appends `id()` and `json()` to the
    // result schema; that's right when serving PowerBI shapes (info_schema.columns reports those
    // pseudo-columns so PowerBI expects them), but wrong when a SQL client explicitly listed
    // its desired columns and would be surprised to find an extra `json()` blob alongside.
    //
    // SELECT * still goes through plain RqlQuery to preserve the all-visible-columns behavior.
    internal sealed class PgSqlTranslatedRqlQuery : RqlQuery
    {
        public PgSqlTranslatedRqlQuery(string queryString, int[] parametersDataTypes, DocumentDatabase documentDatabase, int? limit = null)
            : base(queryString, parametersDataTypes, documentDatabase, limit)
        {
        }

        protected override bool IncludeDocumentIdColumn => false;

        protected override bool IncludePowerBIJsonColumn => false;
    }
}
