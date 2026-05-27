using System.Collections.Generic;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog.Tables
{
    internal sealed class InformationSchemaColumnsTable : PgVirtualTable
    {
        private const string TableNamePredicate = "table_name";
        private const string Yes = "YES";

        public override string SchemaName => "information_schema";
        public override string TableName => "columns";

        public override IReadOnlyList<PgVirtualColumn> Columns { get; } = new PgVirtualColumn[]
        {
            new("column_name",      PgName.Default,    PgFormat.Text),
            new("ordinal_position", PgInt4.Default,    PgFormat.Binary),
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

                int ordinal = 0;
                var propNames = sample.GetPropertyNames();
                foreach (var name in propNames)
                {
                    yield return new object[] { name, ordinal, Yes, string.Empty };
                    ordinal++;
                }
            }
        }
    }
}
