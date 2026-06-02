using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    // Carries a single `<literal> AS <alias>` projection lifted from PowerBI's outer wrapper
    // SQL down to PowerBIRqlQuery, which materializes the literal as a synthetic column
    // appended after the auto-included `json` synthetic. PowerBI's row-preview shapes
    // (`select ..., 1 as "c0" from (inner)`) decorate the outer projection with constants
    // the inner RQL never sees; without this hand-off the response is short one column
    // (`Field count mismatch when mapping column types. N vs N-1`).
    //
    // PgType reflects PG's literal-inference rule (integer literal → int4, etc.) — not the
    // .NET type of Value — because PowerBI's OLE DB provider builds its expected wire schema
    // by parsing its own SQL and will reject a wider/different type.
    public sealed record ConstProjection(string ColumnName, PgType PgType, object Value);
}
