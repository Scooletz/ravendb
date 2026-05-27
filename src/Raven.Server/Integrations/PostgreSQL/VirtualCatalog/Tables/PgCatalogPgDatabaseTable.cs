using System.Collections.Generic;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog.Tables
{
    // pg_database: PostgreSQL's catalog of databases on the server. pgAdmin's connection probe
    // looks here to discover the active database's properties (encoding, can-create, etc.).
    //
    // We expose one row representing the currently-connected RavenDB database. The PG protocol
    // surface is single-DB-per-connection (`Database=Northwind` in the connection string), so
    // there's never anything else to list here. Other databases on the same server are not
    // visible — each one gets its own PG-protocol context.
    internal sealed class PgCatalogPgDatabaseTable : PgVirtualTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_database";

        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("oid",           PgOid.Default,  PgFormat.Text),
            new("datname",       PgName.Default, PgFormat.Text),
            new("datallowconn",  PgBool.Default, PgFormat.Text),
            new("encoding",      PgInt4.Default, PgFormat.Text),
            new("datistemplate", PgBool.Default, PgFormat.Text),
        };

        public override IEnumerable<object[]> EnumerateRows(VirtualQueryContext ctx)
        {
            if (ctx?.Database == null)
                yield break;

            // oid = 1 is arbitrary but non-zero (pgAdmin uses it as a row identifier).
            // encoding = 6 is PG's well-known id for UTF8 (which is all we ever serve).
            yield return new object[] { 1, ctx.Database.Name, true, 6, false };
        }
    }
}
