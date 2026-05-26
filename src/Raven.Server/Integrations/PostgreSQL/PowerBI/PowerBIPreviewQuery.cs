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
using Raven.Server.Logging;
using Sparrow.Logging;
using Sparrow.Server.Logging;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public sealed class PowerBIPreviewQuery : PowerBIRqlQuery
    {
        private static readonly RavenLogger Logger = RavenLogManager.Instance.GetLoggerForServer<PowerBIPreviewQuery>();

        private const string InformationSchema = "information_schema";
        private const string Columns = "columns";

        // Columns this handler can produce. Every projected column in the incoming query must
        // be a member of this set; otherwise we cannot satisfy the client's projection.
        private static readonly System.Collections.Generic.HashSet<string> ProduceableColumns =
            new(StringComparer.OrdinalIgnoreCase) { "COLUMN_NAME", "ORDINAL_POSITION", "IS_NULLABLE", "DATA_TYPE" };

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
                if (SelectStmtShape.TryParseSingleSelect(queryText, out var selectStmt) == false)
                    return false;

                if (IsPowerBiPreviewShape(selectStmt, out var tableName) == false)
                    return false;

                pgQuery = new PowerBIPreviewQuery(documentDatabase, tableName);
                return true;
            }
            catch (Exception e)
            {
                if (Logger.IsDebugEnabled)
                    Logger.Debug($"{nameof(PowerBIPreviewQuery)}.{nameof(TryParse)} rejected query: {e.Message}");
                pgQuery = null;
                return false;
            }
        }

        private static bool IsPowerBiPreviewShape(SelectStmt selectStmt, out string tableName)
        {
            tableName = null;

            if (PgSqlAstHelpers.TryGetSingleRangeVarFromClause(selectStmt, out var rangeVar) == false)
                return false;

            // FROM must be information_schema.columns.
            if (rangeVar.Schemaname.Equals(InformationSchema, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (rangeVar.Relname.Equals(Columns, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            // Every projected column must be one this handler can produce.
            // Flexible about order and aliases; strict about the column set.
            if (PgSqlAstHelpers.ProjectionSubsetOf(selectStmt.TargetList, ProduceableColumns) == false)
                return false;

            // WHERE must contain TABLE_NAME = 'X' — required for execution.
            // TABLE_SCHEMA and any other predicates are optional and ignored.
            return TryExtractTableNameFromWhere(selectStmt.WhereClause, out tableName);
        }

        // Recursively searches the WHERE tree for a TABLE_NAME = '<value>' equality predicate.
        private static bool TryExtractTableNameFromWhere(Node whereNode, out string tableName)
        {
            tableName = null;

            if (whereNode == null)
                return false;

            // Simple equality: TABLE_NAME = 'X'
            var aExpr = whereNode.AExpr;
            if (aExpr != null)
                return TryGetTableNameEquality(aExpr, out tableName);

            // AND / OR expression: recurse into each arg
            var boolExpr = whereNode.BoolExpr;
            if (boolExpr?.Args != null)
            {
                foreach (var arg in boolExpr.Args)
                {
                    if (TryExtractTableNameFromWhere(arg, out tableName))
                        return true;
                }
            }

            return false;
        }

        private static bool TryGetTableNameEquality(A_Expr aExpr, out string tableName)
        {
            tableName = null;

            if (aExpr.Kind != A_Expr_Kind.AexprOp)
                return false;

            if (aExpr.Name is not { Count: 1 } || aExpr.Name[0].String?.Sval != "=")
                return false;

            var colRef = aExpr.Lexpr?.ColumnRef;
            if (colRef?.Fields is not { Count: 1 })
                return false;

            if (colRef.Fields[0].String?.Sval?.Equals("TABLE_NAME", StringComparison.OrdinalIgnoreCase) != true)
                return false;

            tableName = aExpr.Rexpr?.AConst?.Sval?.Sval;
            return string.IsNullOrWhiteSpace(tableName) == false;
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
