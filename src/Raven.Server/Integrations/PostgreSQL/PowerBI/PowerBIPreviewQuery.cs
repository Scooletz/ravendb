using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PgSqlParser;
using Raven.Server.Documents;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public sealed class PowerBIPreviewQuery : PowerBIRqlQuery
    {
        private const string InformationSchema = "information_schema";
        private const string Columns = "columns";
        private const string PublicSchema = "public";

        private readonly List<PgDataRow> _results;

        public PowerBIPreviewQuery(DocumentDatabase documentDatabase, string tableName)
            : base($"from '{tableName}'", Array.Empty<int>(), documentDatabase, limit: 1)
        {
            _results = new List<PgDataRow>();
        }

        public static bool TryParse(string queryText, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            pgQuery = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            try
            {
                if (PgSqlAstHelpers.TryParseSingleSelect(queryText, out var selectStmt) == false)
                    return false;

                if (IsPowerBiPreviewShape(selectStmt, out var tableName) == false)
                    return false;

                pgQuery = new PowerBIPreviewQuery(documentDatabase, tableName);
                return true;
            }
            catch
            {
                pgQuery = null;
                return false;
            }
        }

        private static bool IsPowerBiPreviewShape(SelectStmt selectStmt, out string tableName)
        {
            tableName = null;

            if (PgSqlAstHelpers.TryGetSingleRangeVarFromClause(selectStmt, out var rangeVar) == false)
                return false;

            // FROM
            if (rangeVar.Schemaname.Equals(InformationSchema, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (rangeVar.Relname.Equals(Columns, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            // SELECT
            if (selectStmt.TargetList is not { Count: >= 3 } targets)
                return false;

            if (PgSqlAstHelpers.IsSelectColumn(targets[0], "COLUMN_NAME") == false)
                return false;

            if (PgSqlAstHelpers.IsSelectColumn(targets[1], "ORDINAL_POSITION") == false)
                return false;

            if (PgSqlAstHelpers.IsSelectColumn(targets[2], "IS_NULLABLE") == false)
                return false;

            // WHERE
            if (TryExtractWhereTableSchemaAndName(selectStmt.WhereClause, out var schemaName, out tableName) == false)
                return false;

            if (schemaName.Equals(PublicSchema, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            // ORDER BY
            if (IsPowerBiPreviewOrderBy(selectStmt.SortClause) == false)
                return false;

            return true;
        }

        private static bool TryExtractWhereTableSchemaAndName(Node whereNode, out string schemaName, out string tableName)
        {
            schemaName = null;
            tableName = null;

            if (whereNode?.BoolExpr is not { Boolop: BoolExprType.AndExpr, Args: { Count: 2 } args })
                return false;

            var tableSchemaExpr = args[0]?.AExpr;
            if (tableSchemaExpr == null)
                return false;

            if (tableSchemaExpr.Kind != A_Expr_Kind.AexprOp)
                return false;

            if (tableSchemaExpr.Name is not { Count: 1 } || tableSchemaExpr.Name[0].String?.Sval != "=")
                return false;

            var tableSchemaColRef = tableSchemaExpr.Lexpr?.ColumnRef;
            if (tableSchemaColRef?.Fields is not { Count: 1 })
                return false;

            if (tableSchemaColRef.Fields[0].String?.Sval?.Equals("TABLE_SCHEMA", StringComparison.OrdinalIgnoreCase) != true)
                return false;

            schemaName = tableSchemaExpr.Rexpr?.AConst?.Sval?.Sval;
            if (string.IsNullOrWhiteSpace(schemaName))
                return false;

            var tableNameExpr = args[1]?.AExpr;
            if (tableNameExpr == null)
                return false;

            if (tableNameExpr.Kind != A_Expr_Kind.AexprOp)
                return false;

            if (tableNameExpr.Name is not { Count: 1 } || tableNameExpr.Name[0].String?.Sval != "=")
                return false;

            var tableNameColRef = tableNameExpr.Lexpr?.ColumnRef;
            if (tableNameColRef?.Fields is not { Count: 1 })
                return false;

            if (tableNameColRef.Fields[0].String?.Sval?.Equals("TABLE_NAME", StringComparison.OrdinalIgnoreCase) != true)
                return false;

            tableName = tableNameExpr.Rexpr?.AConst?.Sval?.Sval;
            return string.IsNullOrWhiteSpace(tableName) == false;
        }

        private static bool IsPowerBiPreviewOrderBy(IList<Node> sortClause)
        {
            if (sortClause is not { Count: 3 })
                return false;

            if (PgSqlAstHelpers.IsOrderByAsc(sortClause[0], "TABLE_SCHEMA") == false)
                return false;

            if (PgSqlAstHelpers.IsOrderByAsc(sortClause[1], "TABLE_NAME") == false)
                return false;

            if (PgSqlAstHelpers.IsOrderByAsc(sortClause[2], "ORDINAL_POSITION") == false)
                return false;

            return true;
        }

        public override async Task Execute(MessageBuilder builder, PipeWriter writer, CancellationToken token)
        {
            foreach (var dataRow in _results)
            {
                await writer.WriteAsync(builder.DataRow(dataRow.ColumnData.Span), token);
            }

            await writer.WriteAsync(builder.CommandComplete($"SELECT {_results.Count}"), token);
        }

        public override void Bind(ICollection<byte[]> parameters, short[] parameterFormatCodes, short[] resultColumnFormatCodes)
        {
            // Intentional no-op: this query executes our own RQL (not the original SQL) and currently has no parameters.
        }

        public override async Task<ICollection<PgColumn>> Init(bool allowMultipleStatements = false)
        {
            var schema = await base.Init(allowMultipleStatements);

            int i = 0;
            foreach (var column in schema)
            {
                var columnData = new ReadOnlyMemory<byte>?[]
                {
                    Encoding.UTF8.GetBytes(column.Name), // column_name
                    BitConverter.GetBytes(IPAddress.HostToNetworkOrder(i)), // ordinal_position
                    Encoding.UTF8.GetBytes("YES"), // is_nullable
                    Encoding.UTF8.GetBytes(""), // data_type - easier to leave empty for us
                };

                _results.Add(new PgDataRow(columnData));
                i++;
            }

            return new List<PgColumn>
            {
                new("column_name", 0, PgName.Default,PgFormat.Text),
                new("ordinal_position", 1, PgInt4.Default, PgFormat.Binary),
                new("is_nullable", 2, PgVarchar.Default, PgFormat.Text),
                new("data_type", 3, PgVarchar.Default, PgFormat.Text),
            };
        }
    }
}
