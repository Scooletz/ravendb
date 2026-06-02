using System.Collections.Generic;
using Raven.Server.Documents;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog.Tables
{
    // information_schema.key_column_usage: one row per Raven collection identifying the
    // column that participates in the PRIMARY KEY. For Raven every table's PK is the synthetic
    // `id` column (PG-facing name for the document identifier; see PgSyntheticColumns), at
    // ordinal position 1.
    //
    // Joined by PowerBI to information_schema.table_constraints on (constraint_schema,
    // constraint_name, table_schema, table_name) — both rows here use the same `pk_<TableName>`
    // constraint name so the join produces exactly one row per table for the PK probe.
    internal sealed class InformationSchemaKeyColumnUsageTable : PgVirtualTable
    {
        private const string PublicSchema = "public";
        private const int FirstOrdinalPosition = 1;

        public override string SchemaName => "information_schema";
        public override string TableName => "key_column_usage";

        // Full standard column set. PowerBI's mashup engine reads the entire row internally;
        // narrowing the schema crashes its decoder with `Nullable object must have a value`
        // when it tries to access fields that aren't there. `position_in_unique_constraint`
        // is NULL for PRIMARY KEY rows by PG convention (only set for FK columns).
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("constraint_catalog",            PgName.Default, PgFormat.Binary),
            new("constraint_schema",             PgName.Default, PgFormat.Binary),
            new("constraint_name",               PgName.Default, PgFormat.Binary),
            new("table_catalog",                 PgName.Default, PgFormat.Binary),
            new("table_schema",                  PgName.Default, PgFormat.Binary),
            new("table_name",                    PgName.Default, PgFormat.Binary),
            new("column_name",                   PgName.Default, PgFormat.Binary),
            new("ordinal_position",              PgInt4.Default, PgFormat.Binary),
            new("position_in_unique_constraint", PgInt4.Default, PgFormat.Binary),
        };

        public override IEnumerable<object[]> EnumerateRows(VirtualQueryContext ctx)
        {
            if (ctx?.Database == null)
                yield break;

            var dbName = ctx.Database.Name;

            using (ctx.Database.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
            using (context.OpenReadTransaction())
            {
                foreach (var collection in ctx.Database.DocumentsStorage.GetCollections(context))
                {
                    if (CollectionName.IsHiLoCollection(collection.Name))
                        continue;

                    yield return new object[]
                    {
                        dbName,                          // constraint_catalog
                        PublicSchema,                    // constraint_schema
                        $"pk_{collection.Name}",         // constraint_name
                        dbName,                          // table_catalog
                        PublicSchema,                    // table_schema
                        collection.Name,                 // table_name
                        PgSyntheticColumns.DocumentId,   // column_name
                        FirstOrdinalPosition,            // ordinal_position
                        null,                            // position_in_unique_constraint (only set for FKs)
                    };
                }
            }
        }
    }
}
