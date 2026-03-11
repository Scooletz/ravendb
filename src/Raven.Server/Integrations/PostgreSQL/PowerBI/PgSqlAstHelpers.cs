using System;
using PgSqlParser;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    internal static class PgSqlAstHelpers
    {
        public static bool TryParseSingleSelect(string sql, out SelectStmt selectStmt)
        {
            selectStmt = null;

            if (string.IsNullOrWhiteSpace(sql))
                return false;

            var parseResult = Parser.Parse(sql);
            if (parseResult.IsSuccess == false || parseResult.Value?.Stmts == null)
                return false;

            if (parseResult.Value.Stmts.Count != 1)
                return false;

            selectStmt = parseResult.Value.Stmts[0]?.Stmt?.SelectStmt;
            return selectStmt != null;
        }

        public static bool TryGetSingleRangeVarFromClause(SelectStmt selectStmt, out RangeVar rangeVar)
        {
            rangeVar = null;

            if (selectStmt?.FromClause is not { Count: 1 } fromClause)
                return false;

            rangeVar = fromClause[0]?.RangeVar;
            return rangeVar?.Schemaname != null && rangeVar.Relname != null;
        }

        public static bool IsSelectColumn(Node node, string expectedColumn)
        {
            var colRef = node?.ResTarget?.Val?.ColumnRef;
            return colRef?.Fields is { Count: 1 } fields &&
                   fields[0].String?.Sval?.Equals(expectedColumn, StringComparison.OrdinalIgnoreCase) == true;
        }

        public static bool IsOrderByAsc(Node node, string expectedColumn)
        {
            var sortBy = node?.SortBy;
            if (sortBy == null)
                return false;

            if (sortBy.SortbyDir == SortByDir.SortbyDesc)
                return false;

            var colRef = sortBy.Node?.ColumnRef;
            if (colRef?.Fields is not { Count: 1 } fields)
                return false;

            return fields[0].String?.Sval?.Equals(expectedColumn, StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
