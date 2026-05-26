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

    internal sealed class InformationSchemaReferentialConstraintsTable : EmptyCatalogTable
    {
        public override string SchemaName => "information_schema";
        public override string TableName => "referential_constraints";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("constraint_catalog",        PgName.Default, PgFormat.Binary),
            new("constraint_schema",         PgName.Default, PgFormat.Binary),
            new("constraint_name",           PgName.Default, PgFormat.Binary),
            new("unique_constraint_catalog", PgName.Default, PgFormat.Binary),
            new("unique_constraint_schema",  PgName.Default, PgFormat.Binary),
            new("unique_constraint_name",    PgName.Default, PgFormat.Binary),
            new("match_option",              PgText.Default, PgFormat.Binary),
            new("update_rule",               PgText.Default, PgFormat.Binary),
            new("delete_rule",               PgText.Default, PgFormat.Binary),
        };
    }

    // Empty pg_catalog sources for shapes the interpreter doesn't (yet) read from real data.
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
}
