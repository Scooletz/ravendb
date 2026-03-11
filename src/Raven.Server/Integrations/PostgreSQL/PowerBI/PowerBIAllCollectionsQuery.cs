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
using Raven.Server.ServerWide.Context;
using Node = PgSqlParser.Node;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public sealed class PowerBIAllCollectionsQuery : PowerBIRqlQuery
    {
        private const string InformationSchema = "information_schema";
        private const string Tables = "tables";
        private const string PublicSchema = "public";
        private const string PgCatalog = "pg_catalog";
        private const string BaseTableType = "BASE TABLE";

        private static readonly byte[] PublicSchemaBytes = Encoding.UTF8.GetBytes(PublicSchema);
        private static readonly byte[] BaseTableTypeBytes = Encoding.UTF8.GetBytes(BaseTableType);

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
                if (PgSqlAstHelpers.TryParseSingleSelect(queryText, out var selectStmt) == false)
                    return false;

                if (IsPowerBiAllCollectionsShape(selectStmt) == false)
                    return false;

                pgQuery = new PowerBIAllCollectionsQuery(queryText, parametersDataTypes, documentDatabase);
                return true;
            }
            catch
            {
                pgQuery = null;
                return false;
            }
        }

        private static bool IsPowerBiAllCollectionsShape(SelectStmt selectStmt)
        {
            if (PgSqlAstHelpers.TryGetSingleRangeVarFromClause(selectStmt, out var rangeVar) == false)
                return false;

            // FROM
            if (rangeVar.Schemaname.Equals(InformationSchema, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (rangeVar.Relname.Equals(Tables, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            // SELECT
            if (selectStmt.TargetList is not { Count: 3 } targets)
                return false;

            if (PgSqlAstHelpers.IsSelectColumn(targets[0], "TABLE_SCHEMA") == false)
                return false;

            if (PgSqlAstHelpers.IsSelectColumn(targets[1], "TABLE_NAME") == false)
                return false;

            if (PgSqlAstHelpers.IsSelectColumn(targets[2], "TABLE_TYPE") == false)
                return false;

            // WHERE
            if (TryMatchTableSchemaNotInWhereClause(selectStmt.WhereClause) == false)
                return false;

            // ORDER BY
            if (selectStmt.SortClause is not { Count: 2 } sortClause)
                return false;

            if (PgSqlAstHelpers.IsOrderByAsc(sortClause[0], "TABLE_SCHEMA") == false)
                return false;

            if (PgSqlAstHelpers.IsOrderByAsc(sortClause[1], "TABLE_NAME") == false)
                return false;

            return true;
        }

        private static bool TryMatchTableSchemaNotInWhereClause(Node whereClause)
        {
            var aExpr = whereClause?.AExpr;
            if (aExpr == null)
                return false;

            if (aExpr.Kind != A_Expr_Kind.AexprIn)
                return false;

            // check left side is "TABLE_SCHEMA"
            if (aExpr.Lexpr?.ColumnRef?.Fields is not { Count: 1 } leftFields ||
                leftFields[0].String?.Sval?.Equals("TABLE_SCHEMA", StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (aExpr.Rexpr?.List?.Items is not { Count: 2 } items)
                return false;

            // check values in the list are 'information_schema' and 'pg_catalog'
            string v0 = items[0]?.AConst?.Sval?.Sval;
            string v1 = items[1]?.AConst?.Sval?.Sval;

            if (v0 == null || v1 == null)
                return false;

            var v0IsInfo = v0.Equals(InformationSchema, StringComparison.OrdinalIgnoreCase);
            var v0IsPgCatalog = v0.Equals(PgCatalog, StringComparison.OrdinalIgnoreCase);
            var v1IsInfo = v1.Equals(InformationSchema, StringComparison.OrdinalIgnoreCase);
            var v1IsPgCatalog = v1.Equals(PgCatalog, StringComparison.OrdinalIgnoreCase);

            if ((v0IsInfo && v1IsPgCatalog) == false && (v0IsPgCatalog && v1IsInfo) == false)
                return false;

            // check operator is "<>"
            if (aExpr.Name is not { Count: 1 } || aExpr.Name[0].String?.Sval == null)
                return false;

            return aExpr.Name[0].String.Sval.Equals("<>");
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
