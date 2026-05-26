using System.Collections.Generic;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.Npgsql
{
    /// <summary>
    /// Canonical <see cref="PgTable"/> responses returned to Npgsql clients (3.2+ through 5.x)
    /// for the protocol probes and pg_catalog metadata/type-loading queries that the driver
    /// issues on startup. Queries are classified by shape (see
    /// <see cref="Classification.NpgsqlQueryClassifier"/>); this class just holds the data.
    /// </summary>
    public static class NpgsqlConfig
    {
        // Npgsql 4.1.3–5.x (modern nested shape). Also serves Npgsql 4.x — the two are data-identical.
        public static readonly PgTable Npgsql5TypesResponse = CsvToPg.Convert(
            @"npgsql_types_5.csv",
            new Dictionary<string, PgColumn>
            {
                { "nspname", new PgColumn("nspname", 0, PgName.Default, PgFormat.Text) },
                { "oid", new PgColumn("oid", 1, PgOid.Default, PgFormat.Text) },
                { "typnamespace", new PgColumn("typnamespace", 2, PgOid.Default, PgFormat.Text) },
                { "typname", new PgColumn("typname", 3, PgName.Default, PgFormat.Text) },
                { "typtype", new PgColumn("typtype", 4, PgChar.Default, PgFormat.Text, 1) },
                { "typrelid", new PgColumn("typrelid", 5, PgOid.Default, PgFormat.Text) },
                { "typnotnull", new PgColumn("typnotnull", 6, PgBool.Default, PgFormat.Text) },
                { "relkind", new PgColumn("relkind", 7, PgChar.Default, PgFormat.Text, 1) },
                { "elemtypoid", new PgColumn("elemtypoid", 8, PgOid.Default, PgFormat.Text) },
                { "elemtypname", new PgColumn("elemtypname", 9, PgName.Default, PgFormat.Text) },
                { "elemrelkind", new PgColumn("elemrelkind", 10, PgChar.Default, PgFormat.Text, 1) },
                { "elemtyptype", new PgColumn("elemtyptype", 11, PgChar.Default, PgFormat.Text, 1) },
                { "ord", new PgColumn("ord", 12, PgInt4.Default, PgFormat.Text) },
            });

        // Npgsql 4.1.0–4.1.2 (mid flat shape).
        public static readonly PgTable Npgsql4_1_2TypesResponse = CsvToPg.Convert(
            @"npgsql_types_4_1_2.csv",
            new Dictionary<string, PgColumn>
            {
                { "nspname", new PgColumn("nspname", 0, PgName.Default, PgFormat.Text) },
                { "typname", new PgColumn("typname", 1, PgName.Default, PgFormat.Text) },
                { "oid", new PgColumn("oid", 2, PgOid.Default, PgFormat.Text) },
                { "typbasetype", new PgColumn("typbasetype", 3, PgOid.Default, PgFormat.Text) },
                { "typtype", new PgColumn("typtype", 4, PgChar.Default, PgFormat.Text, 1) },
                { "typelem", new PgColumn("typelem", 5, PgOid.Default, PgFormat.Text) },
                { "ord", new PgColumn("ord", 6, PgInt4.Default, PgFormat.Text) },
            });

        // Npgsql 4.0.1–4.0.12 (old flat shape with pseudo-type arrays). Also serves 4.0.3 — data-identical.
        public static readonly PgTable TypesResponse = CsvToPg.Convert(
            @"types_query.csv",
            new Dictionary<string, PgColumn>
            {
                { "nspname", new PgColumn("nspname", 0, PgName.Default, PgFormat.Text) },
                { "typname", new PgColumn("typname", 1, PgName.Default, PgFormat.Text) },
                { "oid", new PgColumn("oid", 2, PgOid.Default, PgFormat.Text) },
                { "typrelid", new PgColumn("typrelid", 3, PgOid.Default, PgFormat.Text) },
                { "typbasetype", new PgColumn("typbasetype", 4, PgOid.Default, PgFormat.Text) },
                { "type", new PgColumn("type", 5, PgChar.Default, PgFormat.Text, 1) },
                { "elemoid", new PgColumn("elemoid", 6, PgOid.Default, PgFormat.Text) },
                { "ord", new PgColumn("ord", 7, PgInt4.Default, PgFormat.Text) },
            });

        // Npgsql 4.0.0 (old flat shape without pseudo-type arrays).
        public static readonly PgTable Npgsql4_0_0TypesResponse = CsvToPg.Convert(
            @"npgsql_types_4_0_0.csv",
            new Dictionary<string, PgColumn>
            {
                { "nspname", new PgColumn("nspname", 0, PgName.Default, PgFormat.Text) },
                { "typname", new PgColumn("typname", 1, PgName.Default, PgFormat.Text) },
                { "oid", new PgColumn("oid", 2, PgOid.Default, PgFormat.Text) },
                { "typrelid", new PgColumn("typrelid", 3, PgOid.Default, PgFormat.Text) },
                { "typbasetype", new PgColumn("typbasetype", 4, PgOid.Default, PgFormat.Text) },
                { "type", new PgColumn("type", 5, PgChar.Default, PgFormat.Text, 1) },
                { "elemoid", new PgColumn("elemoid", 6, PgOid.Default, PgFormat.Text) },
                { "ord", new PgColumn("ord", 7, PgInt4.Default, PgFormat.Text) },
            });

        // Npgsql 3.2.3–3.2.7 (legacy shape).
        public static readonly PgTable Npgsql3TypesResponse = CsvToPg.Convert(
            @"npgsql_types_3.csv",
            new Dictionary<string, PgColumn>
            {
                { "nspname", new PgColumn("nspname", 0, PgName.Default, PgFormat.Text) },
                { "typname", new PgColumn("typname", 1, PgName.Default, PgFormat.Text) },
                { "oid", new PgColumn("oid", 2, PgOid.Default, PgFormat.Text) },
                { "typrelid", new PgColumn("typrelid", 3, PgOid.Default, PgFormat.Text) },
                { "typbasetype", new PgColumn("typbasetype", 4, PgOid.Default, PgFormat.Text) },
                { "type", new PgColumn("type", 5, PgChar.Default, PgFormat.Text, 1) },
                { "elemoid", new PgColumn("elemoid", 6, PgOid.Default, PgFormat.Text) },
                { "ord", new PgColumn("ord", 7, PgInt4.Default, PgFormat.Text) },
            });
    }
}
