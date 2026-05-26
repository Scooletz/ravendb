using System.Collections.Generic;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog.Tables
{
    internal abstract class EmptyCatalogTable : PgVirtualTable
    {
        public override bool IsAlwaysEmpty => true;
        public override IEnumerable<object[]> EnumerateRows(VirtualQueryContext ctx) => System.Linq.Enumerable.Empty<object[]>();
    }

    // PowerBI metadata sources (queried with PgFormat.Binary).
    internal sealed class InformationSchemaTableConstraintsTable : EmptyCatalogTable
    {
        public override string SchemaName => "information_schema";
        public override string TableName => "table_constraints";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("constraint_schema", PgName.Default, PgFormat.Binary),
            new("constraint_name",   PgName.Default, PgFormat.Binary),
            new("constraint_type",   PgText.Default, PgFormat.Binary),
            new("table_schema",      PgName.Default, PgFormat.Binary),
            new("table_name",        PgName.Default, PgFormat.Binary),
        };
    }

    internal sealed class InformationSchemaKeyColumnUsageTable : EmptyCatalogTable
    {
        public override string SchemaName => "information_schema";
        public override string TableName => "key_column_usage";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("constraint_schema", PgName.Default, PgFormat.Binary),
            new("constraint_name",   PgName.Default, PgFormat.Binary),
            new("table_schema",      PgName.Default, PgFormat.Binary),
            new("table_name",        PgName.Default, PgFormat.Binary),
            new("column_name",       PgName.Default, PgFormat.Binary),
            new("ordinal_position",  PgInt4.Default, PgFormat.Binary),
        };
    }

    // Npgsql pg_catalog sources (queried with PgFormat.Text).
    internal sealed class PgCatalogPgTypeTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_type";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("oid",          PgOid.Default,  PgFormat.Text),
            new("typname",      PgName.Default, PgFormat.Text),
            new("typtype",      PgChar.Default, PgFormat.Text),
            new("typrelid",     PgOid.Default,  PgFormat.Text),
            new("typnamespace", PgOid.Default,  PgFormat.Text),
            new("typbasetype",  PgOid.Default,  PgFormat.Text),
            new("typelem",      PgOid.Default,  PgFormat.Text),
        };
    }

    internal sealed class PgCatalogPgEnumTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_enum";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("oid",            PgOid.Default,  PgFormat.Text),
            new("enumtypid",      PgOid.Default,  PgFormat.Text),
            new("enumlabel",      PgName.Default, PgFormat.Text),
            new("enumsortorder",  PgFloat4.Default, PgFormat.Text),
        };
    }

    internal sealed class PgCatalogPgClassTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_class";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("oid",         PgOid.Default,  PgFormat.Text),
            new("relname",     PgName.Default, PgFormat.Text),
            new("relkind",     PgChar.Default, PgFormat.Text),
            new("typrelid",    PgOid.Default,  PgFormat.Text),
        };
    }

    internal sealed class PgCatalogPgAttributeTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_attribute";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("oid",          PgOid.Default,  PgFormat.Text),
            new("attname",      PgName.Default, PgFormat.Text),
            new("atttypid",     PgOid.Default,  PgFormat.Text),
            new("attrelid",     PgOid.Default,  PgFormat.Text),
            new("attnum",       PgInt2.Default, PgFormat.Text),
            new("attisdropped", PgBool.Default, PgFormat.Text),
        };
    }

    internal sealed class PgCatalogPgNamespaceTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_namespace";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("oid",     PgOid.Default,  PgFormat.Text),
            new("nspname", PgName.Default, PgFormat.Text),
        };
    }
}
