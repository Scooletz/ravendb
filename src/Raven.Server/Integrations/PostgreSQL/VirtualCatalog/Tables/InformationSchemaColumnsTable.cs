using System.Collections.Generic;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Sparrow.Json;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog.Tables
{
    // information_schema.columns: per-column metadata for every "table" the PG endpoint exposes.
    // Used by PowerBI's data-loader and pgAdmin's schema browser to discover a collection's column
    // shape before issuing the actual SELECT.
    //
    // CRITICAL: the column list we report here MUST match exactly what RqlQuery emits in its
    // RowDescription when serving the actual data query. If they disagree (different count, order,
    // names, or types), PowerBI's Mashup engine compares the two and raises `DataSource.Changed:
    // The data source appears to have been modified since it was last accessed.` Specifically:
    //
    //   - Order: RqlQuery uses `GetPropertyNames()` which returns properties in DOCUMENT
    //     INSERTION ORDER (sorted by byte offset). We must do the same; `GetPropertyByIndex(i)`
    //     gives a different (property-id / alphabetical) order and breaks the contract.
    //   - Auto-columns: RqlQuery prepends `id()` and appends `json()` for every collection query.
    //     We have to bracket the user properties with the same pseudo-columns.
    //   - Types: must mirror RqlQuery's BlittableJsonToken → PgType mapping exactly (see MapDataType),
    //     including the datetime-shaped-string → timestamp promotion. A type mismatch makes PowerBI read
    //     a column as text from the probe but timestamp from the data query, breaking date filters.
    //
    // Schema/catalog identity exposed in the rows:
    //   table_catalog = ctx.Database.Name (each RavenDB DB hosts one PG "catalog")
    //   table_schema  = "public"          (PG default; we don't model multiple schemas)
    internal sealed class InformationSchemaColumnsTable : PgVirtualTable
    {
        private const string TableNamePredicate = "table_name";
        private const string Yes = "YES";

        public override string SchemaName => "information_schema";
        public override string TableName => "columns";

        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("table_catalog",    PgName.Default,    PgFormat.Text),
            new("table_schema",     PgName.Default,    PgFormat.Text),
            new("table_name",       PgName.Default,    PgFormat.Text),
            new("column_name",      PgName.Default,    PgFormat.Text),
            new("ordinal_position", PgInt4.Default,    PgFormat.Text),
            new("is_nullable",      PgVarchar.Default, PgFormat.Text),
            new("data_type",        PgVarchar.Default, PgFormat.Text),
        };

        public override IEnumerable<object[]> EnumerateRows(VirtualQueryContext ctx)
        {
            if (ctx?.Database == null)
                yield break;

            if (ctx.Predicates == null ||
                ctx.Predicates.TryGetValue(TableNamePredicate, out var rawTable) == false ||
                rawTable is not string collection ||
                string.IsNullOrWhiteSpace(collection))
                yield break;

            using (ctx.Database.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
            using (context.OpenReadTransaction())
            {
                BlittableJsonReaderObject sample = null;
                foreach (var doc in ctx.Database.DocumentsStorage.GetDocumentsFrom(context, collection, etag: 0, start: 0, take: 1))
                {
                    sample = doc.Data;
                    break;
                }

                if (sample == null)
                    yield break;

                var dbName = ctx.Database.Name;
                int ordinal = 1;

                // 1) Synthetic id column (PG-facing name; see PgSyntheticColumns).
                yield return new object[]
                {
                    dbName, "public", collection,
                    PgSyntheticColumns.DocumentId,
                    ordinal++, Yes, "text"
                };

                // 2) User columns in document insertion order (same order RqlQuery emits).
                var prop = default(BlittableJsonReaderObject.PropertyDetails);
                foreach (var name in sample.GetPropertyNames())
                {
                    if (string.IsNullOrEmpty(name))
                        continue;
                    // Skip RavenDB system fields (@metadata, etc.) — RqlQuery skips them too.
                    if (name.StartsWith('@'))
                        continue;

                    var propIdx = sample.GetPropertyIndex(name);
                    if (propIdx == -1)
                        continue;
                    sample.GetPropertyByIndex(propIdx, ref prop);

                    var dataType = MapDataType(prop.Token, prop.Value);
                    yield return new object[] { dbName, "public", collection, name, ordinal++, Yes, dataType };
                }

                // 3) json — RqlQuery appends this as the metadata blob column (PgJson). Always
                //    last in RowDescription, so it goes last here too. PG-facing name as above.
                yield return new object[]
                {
                    dbName, "public", collection,
                    PgSyntheticColumns.Json,
                    ordinal++, Yes, "json"
                };
            }
        }

        // Mirrors RqlQuery.GenerateSchema's BlittableJsonToken → PgType decision tree so the
        // data_type strings here match the PG types of the corresponding columns in RqlQuery's
        // RowDescription. Drift here triggers PowerBI's DataSource.Changed error or — when the
        // column-probe types disagree with the data-query types — silent type mismatches in M
        // filters (e.g. `[OrderedAt] >= RangeStart` fails because the column reads as text).
        // For String/CompressedString tokens we additionally peek at the value via
        // TypeConverter, matching the same value-inspection promotion RqlQuery applies —
        // datetime-shaped strings become timestamp, not text.
        private static string MapDataType(BlittableJsonToken token, object value)
        {
            var bjt = token & BlittableJsonToken.TypesMask;

            if (bjt is BlittableJsonToken.String or BlittableJsonToken.CompressedString)
            {
                var processedString = bjt == BlittableJsonToken.CompressedString
                    ? (string)(LazyCompressedStringValue)value
                    : (string)(LazyStringValue)value;

                if (processedString != null
                    && TypeConverter.TryConvertStringValue(processedString, out var parsed))
                {
                    return parsed switch
                    {
                        // RqlQuery: PgTimestamp / PgTimestampTz / PgInterval
                        System.DateTime dt   => dt.Kind == System.DateTimeKind.Utc
                                                    ? "timestamp with time zone"
                                                    : "timestamp without time zone",
                        System.DateTimeOffset => "timestamp with time zone",
                        System.TimeSpan       => "interval",
                        _                     => "text"
                    };
                }

                return "text";   // RqlQuery: PgText
            }

            return bjt switch
            {
                BlittableJsonToken.Integer           => "bigint",            // RqlQuery: PgInt8
                BlittableJsonToken.LazyNumber        => "double precision",  // RqlQuery: PgFloat8
                BlittableJsonToken.Boolean           => "boolean",           // RqlQuery: PgBool
                BlittableJsonToken.StartObject       => "json",              // RqlQuery: PgJson
                BlittableJsonToken.StartArray        => "json",              // RqlQuery: PgJson
                BlittableJsonToken.EmbeddedBlittable => "json",              // RqlQuery: PgJson
                BlittableJsonToken.Null              => "json",              // RqlQuery: PgJson
                _                                    => "json",              // unknown → PgJson
            };
        }
    }
}
