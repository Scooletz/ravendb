using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PgSqlParser;
using Raven.Server.Documents;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;
using Raven.Server.Logging;
using Raven.Server.ServerWide.Context;
using Sparrow.Logging;
using Sparrow.Server.Logging;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public sealed class PowerBIAllCollectionsQuery : PowerBIRqlQuery
    {
        private static readonly RavenLogger Logger = RavenLogManager.Instance.GetLoggerForServer<PowerBIAllCollectionsQuery>();

        private const string InformationSchema = "information_schema";
        private const string Tables = "tables";
        private const string PublicSchema = "public";
        private const string BaseTableType = "BASE TABLE";

        private static readonly byte[] PublicSchemaBytes = Encoding.UTF8.GetBytes(PublicSchema);
        private static readonly byte[] BaseTableTypeBytes = Encoding.UTF8.GetBytes(BaseTableType);

        // Columns this handler can produce. Every projected column in the incoming query must
        // be a member of this set; otherwise we cannot satisfy the client's projection.
        private static readonly System.Collections.Generic.HashSet<string> ProduceableColumns =
            new(StringComparer.OrdinalIgnoreCase) { "TABLE_SCHEMA", "TABLE_NAME", "TABLE_TYPE" };

        public PowerBIAllCollectionsQuery(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase)
            : base(queryText, parametersDataTypes, documentDatabase)
        {
        }

        public static bool TryParse(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            pgQuery = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            try
            {
                if (SelectStmtShape.TryParseSingleSelect(queryText, out var selectStmt) == false)
                    return false;

                if (IsPowerBiAllCollectionsShape(selectStmt) == false)
                    return false;

                pgQuery = new PowerBIAllCollectionsQuery(queryText, parametersDataTypes, documentDatabase);
                return true;
            }
            catch (Exception e)
            {
                if (Logger.IsDebugEnabled)
                    Logger.Debug($"{nameof(PowerBIAllCollectionsQuery)}.{nameof(TryParse)} rejected query: {e.Message}");
                pgQuery = null;
                return false;
            }
        }

        private static bool IsPowerBiAllCollectionsShape(SelectStmt selectStmt)
        {
            if (PgSqlAstHelpers.TryGetSingleRangeVarFromClause(selectStmt, out var rangeVar) == false)
                return false;

            // FROM must be information_schema.tables.
            if (rangeVar.Schemaname.Equals(InformationSchema, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (rangeVar.Relname.Equals(Tables, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            // Every projected column must be one this handler can produce.
            // Flexible about order and aliases; strict about the column set.
            // ORDER BY and WHERE are ignored — we always return the full collection list.
            return PgSqlAstHelpers.ProjectionSubsetOf(selectStmt.TargetList, ProduceableColumns);
        }

        public override Task<ICollection<PgColumn>> Init(bool allowMultipleStatements = false)
        {
            return Task.FromResult<ICollection<PgColumn>>(new PgColumn[]
            {
                new("table_schema", 0, PgName.Default, PgFormat.Text),
                new("table_name", 1, PgName.Default, PgFormat.Text),
                new("table_type", 2, PgVarchar.Default, PgFormat.Text),
            });
        }

        public override async Task Execute(MessageBuilder builder, PipeWriter writer, CancellationToken token)
        {
            var collections = new List<string>();

            using (DocumentDatabase.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
            using (context.OpenReadTransaction())
            {
                foreach (var collection in DocumentDatabase.DocumentsStorage.GetCollections(context))
                {
                    if (CollectionName.IsHiLoCollection(collection.Name))
                        continue;

                    collections.Add(collection.Name);
                }
            }

            foreach (var collection in collections)
            {
                var dataRow = new ReadOnlyMemory<byte>?[]
                {
                    PublicSchemaBytes,
                    Encoding.UTF8.GetBytes(collection),
                    BaseTableTypeBytes,
                };

                await writer.WriteAsync(builder.DataRow(dataRow), token);
            }

            await writer.WriteAsync(builder.CommandComplete($"SELECT {collections.Count}"), token);
        }
    }
}
