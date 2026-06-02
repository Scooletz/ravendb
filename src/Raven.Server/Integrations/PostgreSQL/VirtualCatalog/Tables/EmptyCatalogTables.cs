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

    // InformationSchemaTableConstraintsTable and InformationSchemaKeyColumnUsageTable moved
    // into their own files — they're now populated per Raven collection to expose the synthetic
    // `id` primary key. PowerBI's DirectQuery mashup engine needs PK metadata to build per-row
    // identity; an empty table_constraints view causes SubstituteWithIndex failures for any
    // visual / slicer / relationship that needs row substitution.

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

    // RavenDB has no PG extensions; an empty table lets pgAdmin's `count(extname)` probe return 0.
    internal sealed class PgCatalogPgExtensionTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_extension";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("oid",        PgOid.Default,  PgFormat.Text),
            new("extname",    PgName.Default, PgFormat.Text),
            new("extversion", PgText.Default, PgFormat.Text),
        };
    }

    // No replication on RavenDB's PG surface; pgAdmin's `count(*)` over this returns 0.
    internal sealed class PgCatalogPgReplicationSlotsTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_replication_slots";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("slot_name", PgName.Default, PgFormat.Text),
            new("slot_type", PgText.Default, PgFormat.Text),
            new("active",    PgBool.Default, PgFormat.Text),
        };
    }

    // GSSAPI authentication status. We don't support GSSAPI, so the view is empty and pgAdmin's
    // `WHERE pid = pg_backend_pid()` filter yields no rows (which pgAdmin treats as "no GSSAPI").
    internal sealed class PgCatalogPgStatGssapiTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_stat_gssapi";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("pid",                PgInt4.Default, PgFormat.Text),
            new("gss_authenticated",  PgBool.Default, PgFormat.Text),
            new("encrypted",          PgBool.Default, PgFormat.Text),
        };
    }

    // RavenDB has no PG role hierarchy — every connected user is independent. pg_auth_members
    // (which lists role-group memberships) is therefore empty; pgAdmin's recursive role-membership
    // CTE iterates against this empty table and terminates with just the base case.
    internal sealed class PgCatalogPgAuthMembersTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_auth_members";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("roleid",       PgOid.Default,  PgFormat.Text),
            new("member",       PgOid.Default,  PgFormat.Text),
            new("grantor",      PgOid.Default,  PgFormat.Text),
            new("admin_option", PgBool.Default, PgFormat.Text),
        };
    }

    // RavenDB doesn't model tablespaces, but pgAdmin LEFT-JOINs pg_database against this view to
    // get the spacename for display. Empty → spcname stays NULL, which is fine.
    internal sealed class PgCatalogPgTablespaceTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_tablespace";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("oid",     PgOid.Default,  PgFormat.Text),
            new("spcname", PgName.Default, PgFormat.Text),
        };
    }

    // Shared-object comments (cluster-wide objects like databases). We don't model comments;
    // pgAdmin LEFT-JOINs to pull descriptions and accepts NULL when there's no match.
    internal sealed class PgCatalogPgShdescriptionTable : EmptyCatalogTable
    {
        public override string SchemaName => "pg_catalog";
        public override string TableName => "pg_shdescription";
        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("objoid",      PgOid.Default,  PgFormat.Text),
            new("classoid",    PgOid.Default,  PgFormat.Text),
            new("description", PgText.Default, PgFormat.Text),
        };
    }
}
