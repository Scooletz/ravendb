using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    // A single `<literal> AS <alias>` projection lifted from PowerBI's outer wrapper —
    // materialized as a synthetic column so the response width matches what PowerBI expects.
    // PgType uses PG's literal-inference rule (unadorned `1` is int4, not int8) since PowerBI's
    // OLE DB provider rejects a wider type than its own SQL parser predicted.
    public sealed record ConstProjection(string ColumnName, PgType PgType, object Value);
}
