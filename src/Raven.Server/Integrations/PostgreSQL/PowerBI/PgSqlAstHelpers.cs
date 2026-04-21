using System;
using System.Collections.Generic;
using PgSqlParser;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    internal static class PgSqlAstHelpers
    {
        public static bool IsPowerBiWrapperAlias(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return false;

            return string.Equals(alias, "$Table", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(alias, "_", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(alias, "rows", StringComparison.OrdinalIgnoreCase);
        }


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

        /// <summary>
        /// Returns true iff every projected target in <paramref name="targetList"/> refers to a column
        /// that is a member of <paramref name="produceable"/>.
        ///
        /// Rules per target:
        /// - Simple column reference (with or without alias): check the underlying column name.
        /// - Complex expression (CASE, function, …) with an explicit alias: check the alias.
        /// - Complex expression without an alias, or empty target list: reject.
        ///
        /// This lets handlers accept reordered / aliased projections while refusing any column
        /// the handler cannot actually produce.
        /// </summary>
        public static bool ProjectionSubsetOf(IList<Node> targetList, HashSet<string> produceable)
        {
            if (targetList == null || targetList.Count == 0)
                return false;

            foreach (var node in targetList)
            {
                var rt = node?.ResTarget;
                if (rt == null)
                    return false;

                var colRef = rt.Val?.ColumnRef;
                if (colRef != null)
                {
                    // Column reference (possibly qualified: tbl.col). Use the last field as the name.
                    var name = colRef.Fields?[^1]?.String?.Sval;
                    if (string.IsNullOrWhiteSpace(name) || produceable.Contains(name) == false)
                        return false;
                }
                else
                {
                    // Complex expression (CASE, function call, cast, …): require an explicit alias
                    // whose name is in the produceable set so we know what column this maps to.
                    if (string.IsNullOrWhiteSpace(rt.Name) || produceable.Contains(rt.Name) == false)
                        return false;
                }
            }

            return true;
        }
    }
}
