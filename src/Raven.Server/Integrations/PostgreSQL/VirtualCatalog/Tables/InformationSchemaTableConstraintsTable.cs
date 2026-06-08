using System.Collections.Generic;
using Raven.Server.Documents;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog.Tables
{
    // information_schema.table_constraints: one PRIMARY KEY row per Raven collection. The PK
    // column itself is reported via the sibling key_column_usage view; PowerBI joins the two on
    // (constraint_schema, constraint_name, table_schema, table_name) to discover that `id` is
    // the primary key of every Raven table.
    //
    // Without this, PowerBI's DirectQuery mashup engine sees "table has no primary key", fails
    // to establish per-row identity, and raises `SubstituteWithIndex detected more than one row
    // in the index table matching to the current row of the original table` for any visual,
    // slicer, or relationship that needs unique row substitution.
    //
    // The PK column name we report (`id`) is the PG-idiomatic surface name — PG identifiers
    // cannot contain parens unquoted, so reporting `id()` (the RQL function-call form) here
    // would parse-fail in PowerBI's mashup engine and trigger `Nullable object must have a
    // value` during model construction. See PgSyntheticColumns for the rename detail.
    internal sealed class InformationSchemaTableConstraintsTable : PgVirtualTable
    {
        private const string PublicSchema = "public";
        private const string PrimaryKeyType = "PRIMARY KEY";
        private const string No = "NO";
        private const string Yes = "YES";

        public override string SchemaName => "information_schema";
        public override string TableName => "table_constraints";

        // Full standard column set (PG 15+). Even though PowerBI's join query only references
        // a subset, the mashup engine reads the full row internally — reporting a narrower
        // schema crashes its decoder with `Nullable object must have a value` when it tries
        // to access fields that aren't there.
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("constraint_catalog", PgName.Default,    PgFormat.Text),
            new("constraint_schema",  PgName.Default,    PgFormat.Text),
            new("constraint_name",    PgName.Default,    PgFormat.Text),
            new("table_catalog",      PgName.Default,    PgFormat.Text),
            new("table_schema",       PgName.Default,    PgFormat.Text),
            new("table_name",         PgName.Default,    PgFormat.Text),
            new("constraint_type",    PgVarchar.Default, PgFormat.Text),
            new("is_deferrable",      PgVarchar.Default, PgFormat.Text),
            new("initially_deferred", PgVarchar.Default, PgFormat.Text),
            new("enforced",           PgVarchar.Default, PgFormat.Text),
            new("nulls_distinct",     PgVarchar.Default, PgFormat.Text),
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

                    // Synthetic constraint name `pk_<TableName>` joins to the matching row in
                    // key_column_usage. Every Raven collection has exactly one PK (the doc id),
                    // so no UNIQUE-constraint rows are emitted.
                    yield return new object[]
                    {
                        dbName,                       // constraint_catalog
                        PublicSchema,                 // constraint_schema
                        $"pk_{collection.Name}",      // constraint_name
                        dbName,                       // table_catalog
                        PublicSchema,                 // table_schema
                        collection.Name,              // table_name
                        PrimaryKeyType,               // constraint_type
                        No,                           // is_deferrable
                        No,                           // initially_deferred
                        Yes,                          // enforced
                        Yes,                          // nulls_distinct
                    };
                }
            }
        }
    }
}
