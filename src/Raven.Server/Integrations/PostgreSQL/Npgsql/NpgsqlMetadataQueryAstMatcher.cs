using System;
using System.Collections.Generic;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL.Npgsql
{
    /// <summary>
    /// AST-based matcher for Npgsql pg_catalog metadata queries whose response schema is identical
    /// across all driver versions: enum types (<c>pg_enum</c>) and composite types (<c>pg_attribute</c>).
    /// Version variants differ only in comment style or ORDER BY; the parser strips both.
    /// </summary>
    internal static class NpgsqlMetadataQueryAstMatcher
    {
        public static bool TryMatch(string queryText, out PgTable result)
        {
            result = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            var parseResult = Parser.Parse(queryText);
            if (parseResult.IsSuccess == false || parseResult.Value?.Stmts is not { Count: 1 })
                return false;

            var select = parseResult.Value.Stmts[0]?.Stmt?.SelectStmt;
            if (select == null)
                return false;

            if (IsEnumTypesQuery(select))
            {
                // Both the old (block-comment) and new (line-comment) variants parse identically;
                // both return the same schema, so a single canonical response covers all versions.
                result = NpgsqlConfig.EnumTypesResponse;
                return true;
            }

            if (IsCompositeTypesQuery(select))
            {
                // All three composite variants return the same schema.
                result = NpgsqlConfig.CompositeTypesResponse;
                return true;
            }

            return false;
        }

        // Anchor: pg_enum in FROM + exactly {oid, enumlabel} in SELECT.
        private static bool IsEnumTypesQuery(SelectStmt s)
        {
            if (s.FromClause == null)
                return false;

            if (ContainsTable(s, "pg_enum") == false)
                return false;

            if (ContainsTable(s, "pg_type") == false)
                return false;

            return HasExactTargetColumnSet(s, "oid", "enumlabel");
        }

        // Anchor: pg_attribute in FROM + exactly {oid, attname, atttypid} in SELECT.
        private static bool IsCompositeTypesQuery(SelectStmt s)
        {
            if (s.FromClause == null)
                return false;

            if (ContainsTable(s, "pg_attribute") == false)
                return false;

            if (ContainsTable(s, "pg_class") == false)
                return false;

            return HasExactTargetColumnSet(s, "oid", "attname", "atttypid");
        }

        // Checks count and last-field name for each target (strips table prefix; order-independent).
        private static bool HasExactTargetColumnSet(SelectStmt s, params string[] expected)
        {
            if (s.TargetList?.Count != expected.Length)
                return false;

            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in s.TargetList)
            {
                var rt = node?.ResTarget;
                if (rt == null)
                    return false;

                string name;
                if (string.IsNullOrWhiteSpace(rt.Name) == false)
                {
                    // Explicit alias takes precedence.
                    name = rt.Name;
                }
                else
                {
                    // Unaliased column reference: use the last field (strips any table prefix).
                    var colRef = rt.Val?.ColumnRef;
                    if (colRef?.Fields == null || colRef.Fields.Count == 0)
                        return false;

                    name = colRef.Fields[^1]?.String?.Sval;
                    if (string.IsNullOrWhiteSpace(name))
                        return false;
                }

                found.Add(name);
            }

            foreach (var col in expected)
            {
                if (found.Contains(col) == false)
                    return false;
            }

            return true;
        }

        private static bool ContainsTable(SelectStmt s, string tableName)
        {
            if (s?.FromClause == null)
                return false;

            foreach (var from in s.FromClause)
            {
                if (NodeContainsTable(from, tableName))
                    return true;
            }

            return false;
        }

        private static bool NodeContainsTable(Node node, string tableName)
        {
            if (node == null)
                return false;

            var rv = node.RangeVar;
            if (rv != null && string.Equals(rv.Relname, tableName, StringComparison.OrdinalIgnoreCase))
                return true;

            var join = node.JoinExpr;
            if (join != null)
            {
                if (NodeContainsTable(join.Larg, tableName))
                    return true;
                if (NodeContainsTable(join.Rarg, tableName))
                    return true;
            }

            return false;
        }
    }
}
