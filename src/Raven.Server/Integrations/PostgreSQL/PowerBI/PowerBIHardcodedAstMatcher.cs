using System;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    internal static class PowerBIHardcodedAstMatcher
    {
        public static bool TryMatchPowerBIHardcodedQuery(string queryText, out PgTable result)
        {
            result = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            var parseResult = Parser.Parse(queryText);
            if (parseResult.IsSuccess == false || parseResult.Value?.Stmts is not { Count: 1 })
                return false;

            var selectStmt = parseResult.Value.Stmts[0]?.Stmt?.SelectStmt;
            if (selectStmt == null)
                return false;

            if (TryMatchCharacterSets(selectStmt))
            {
                result = PowerBIConfig.CharacterSetsResponse;
                return true;
            }

            if (TryMatchConstraints(selectStmt))
            {
                result = PowerBIConfig.ConstraintsResponse;
                return true;
            }

            if (TryMatchTableSchema(selectStmt))
            {
                result = PowerBIConfig.TableSchemaResponse;
                return true;
            }

            if (TryMatchTableSchemaSecondary(selectStmt))
            {
                result = PowerBIConfig.TableSchemaSecondaryResponse;
                return true;
            }

            if (TryMatchReferentialConstraintsFk(selectStmt))
            {
                // RavenDB has no SQL foreign keys, so the correct answer is always empty rows
                // with the same schema shape that PowerBI expects for its FK metadata query.
                result = PowerBIConfig.TableSchemaResponse;
                return true;
            }

            return false;
        }

        private static bool TryMatchCharacterSets(SelectStmt s)
        {
            if (s.FromClause is not { Count: 1 })
                return false;

            if (TryGetFromTable(s.FromClause[0], out var schema, out var table) == false)
                return false;

            if (IsInformationSchema(schema) == false)
                return false;

            if (string.Equals(table, "character_sets", StringComparison.OrdinalIgnoreCase) == false)
                return false;

            return HasSingleTargetColumn(s, "character_set_name");
        }

        private static bool TryMatchConstraints(SelectStmt s)
        {
            if (s.FromClause == null)
                return false;

            // Note: we only validate a strong signature (schemas + key information_schema tables).
            if (ContainsFromTable(s, "information_schema", "table_constraints") == false)
                return false;

            if (ContainsFromTable(s, "information_schema", "key_column_usage") == false)
                return false;

            // Must project the expected columns (Power BI metadata shape).
            return HasTargetColumns(s, "index_name", "column_name", "ordinal_position", "primary_key");
        }

        private static bool TryMatchTableSchema(SelectStmt s)
        {
            if (s.FromClause == null)
                return false;

            // Strong signature: INFORMATION_SCHEMA columns + table_constraints are present.
            if (ContainsFromTable(s, "information_schema", "key_column_usage") == false)
                return false;

            if (ContainsFromTable(s, "information_schema", "table_constraints") == false)
                return false;

            if (s.TargetList is not { Count: 6 })
                return false;

            if (TryMatchTargetColumn(s.TargetList[0]?.ResTarget, expectedAlias: "pk_column_name", expectedColumn: "column_name") == false)
                return false;
            if (TryMatchTargetColumn(s.TargetList[1]?.ResTarget, expectedAlias: "fk_table_schema", expectedColumn: "table_schema") == false)
                return false;
            if (TryMatchTargetColumn(s.TargetList[2]?.ResTarget, expectedAlias: "fk_table_name", expectedColumn: "table_name") == false)
                return false;
            if (TryMatchTargetColumn(s.TargetList[3]?.ResTarget, expectedAlias: "fk_column_name", expectedColumn: "column_name") == false)
                return false;
            if (TryMatchTargetColumn(s.TargetList[4]?.ResTarget, expectedAlias: "ordinal", expectedColumn: "ordinal_position") == false)
                return false;

            // Final FK name: allow either explicit alias or aliasless computed expression.
            var last = s.TargetList[5]?.ResTarget;
            return TryMatchFkNameTarget(last);
        }

        private static bool TryMatchTableSchemaSecondary(SelectStmt s)
        {
            if (s.FromClause == null)
                return false;

            if (ContainsFromTable(s, "information_schema", "key_column_usage") == false)
                return false;

            if (ContainsFromTable(s, "information_schema", "table_constraints") == false)
                return false;

            if (s.TargetList is not { Count: 6 })
                return false;

            if (TryMatchTargetColumn(s.TargetList[0]?.ResTarget, expectedAlias: "pk_table_schema", expectedColumn: "table_schema") == false)
                return false;
            if (TryMatchTargetColumn(s.TargetList[1]?.ResTarget, expectedAlias: "pk_table_name", expectedColumn: "table_name") == false)
                return false;
            if (TryMatchTargetColumn(s.TargetList[2]?.ResTarget, expectedAlias: "pk_column_name", expectedColumn: "column_name") == false)
                return false;
            if (TryMatchTargetColumn(s.TargetList[3]?.ResTarget, expectedAlias: "fk_column_name", expectedColumn: "column_name") == false)
                return false;
            if (TryMatchTargetColumn(s.TargetList[4]?.ResTarget, expectedAlias: "ordinal", expectedColumn: "ordinal_position") == false)
                return false;

            var last = s.TargetList[5]?.ResTarget;
            return TryMatchFkNameTarget(last);
        }

        private static bool TryMatchTargetColumn(ResTarget rt, string expectedAlias, string expectedColumn)
        {
            if (rt == null)
                return false;

            if (string.IsNullOrWhiteSpace(rt.Name) || string.Equals(rt.Name, expectedAlias, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            var colRef = rt.Val?.ColumnRef;
            if (colRef?.Fields == null || colRef.Fields.Count == 0)
                return false;

            var name = colRef.Fields[^1]?.String?.Sval;
            return string.Equals(name, expectedColumn, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryMatchFkNameTarget(ResTarget rt)
        {
            if (rt == null)
                return false;

            if (string.IsNullOrWhiteSpace(rt.Name) == false)
                return string.Equals(rt.Name, "fk_name", StringComparison.OrdinalIgnoreCase);

            // TableSchemaSecondaryQuery can project just `fkcon.constraint_schema` without an alias.
            if (rt.Val?.ColumnRef?.Fields is { Count: > 0 } fields)
            {
                var name = fields[^1]?.String?.Sval;
                if (string.Equals(name, "constraint_schema", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Aliasless computed FK name: accept only concatenation-like expression shapes.
            // PowerBI uses: fkcon.CONSTRAINT_SCHEMA || '_' || <col>
            var v = rt.Val;
            if (v == null)
                return false;

            if (v.AExpr != null)
                return true;

            // Some parser variants may wrap the AExpr.
            if (v.TypeCast?.Arg?.AExpr != null)
                return true;

            return false;
        }

        private static bool HasSingleTargetColumn(SelectStmt s, string columnName)
        {
            if (s.TargetList is not { Count: 1 })
                return false;

            var rt = s.TargetList[0]?.ResTarget;
            if (rt == null)
                return false;

            if (TryExtractColumnName(rt, out var name) == false)
                return false;

            return string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasTargetColumns(SelectStmt s, params string[] expectedAliases)
        {
            if (s.TargetList == null || s.TargetList.Count == 0)
                return false;

            var aliases = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in s.TargetList)
            {
                var rt = t?.ResTarget;
                if (rt == null)
                    return false;

                if (TryExtractTargetAliasOrColumn(rt, out var alias) == false)
                    return false;

                aliases.Add(alias);
            }

            foreach (var e in expectedAliases)
            {
                if (aliases.Contains(e) == false)
                    return false;
            }

            return true;
        }

        private static bool TryExtractTargetAliasOrColumn(ResTarget rt, out string alias)
        {
            alias = null;

            if (string.IsNullOrWhiteSpace(rt.Name) == false)
            {
                alias = rt.Name;
                return true;
            }

            return TryExtractColumnName(rt, out alias);
        }

        private static bool TryExtractColumnName(ResTarget rt, out string name)
        {
            name = null;

            var colRef = rt.Val?.ColumnRef;
            if (colRef?.Fields == null || colRef.Fields.Count == 0)
                return false;

            name = colRef.Fields[^1]?.String?.Sval;
            return string.IsNullOrWhiteSpace(name) == false;
        }

        private static bool ContainsFromTable(SelectStmt s, string schema, string table)
        {
            if (s == null)
                return false;

            if (s.FromClause != null)
            {
                foreach (var from in s.FromClause)
                {
                    if (TryGetFromTable(from, out var foundSchema, out var foundTable) &&
                        string.Equals(foundTable, table, StringComparison.OrdinalIgnoreCase) &&
                        (schema == null || string.Equals(foundSchema, schema, StringComparison.OrdinalIgnoreCase)))
                        return true;

                    if (TryGetJoinTables(from?.JoinExpr, schema, table))
                        return true;
                }
            }

            return false;

            static bool TryGetJoinTables(JoinExpr joinExpr, string schema, string table)
            {
                if (joinExpr == null)
                    return false;

                if (TryGetFromTable(joinExpr.Larg, out var leftSchema, out var leftTable) &&
                    string.Equals(leftTable, table, StringComparison.OrdinalIgnoreCase) &&
                    (schema == null || string.Equals(leftSchema, schema, StringComparison.OrdinalIgnoreCase)))
                    return true;

                if (TryGetFromTable(joinExpr.Rarg, out var rightSchema, out var rightTable) &&
                    string.Equals(rightTable, table, StringComparison.OrdinalIgnoreCase) &&
                    (schema == null || string.Equals(rightSchema, schema, StringComparison.OrdinalIgnoreCase)))
                    return true;

                return TryGetJoinTables(joinExpr.Larg?.JoinExpr, schema, table) ||
                       TryGetJoinTables(joinExpr.Rarg?.JoinExpr, schema, table);
            }
        }

        /// <summary>
        /// Matches the Power BI FK metadata query that uses INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
        /// (inside a subquery) together with KEY_COLUMN_USAGE JOINs.  This is a newer Power BI Desktop
        /// variant of the FK-schema probe; the separator in the FK_NAME expression may vary ('*' or '_').
        /// RavenDB has no real foreign keys, so the response is always the same empty-row schema table.
        /// </summary>
        private static bool TryMatchReferentialConstraintsFk(SelectStmt s)
        {
            if (s.FromClause == null)
                return false;

            // Must reference KEY_COLUMN_USAGE in the JOIN tree (appears twice: fkcol and pkcol).
            if (ContainsFromTable(s, "information_schema", "key_column_usage") == false)
                return false;

            // Distinguishing signature: REFERENTIAL_CONSTRAINTS is used inside a subquery in the FROM clause.
            if (ContainsSubqueryReferencingTable(s, "information_schema", "referential_constraints") == false)
                return false;

            // All known Power BI FK metadata variants project exactly 6 columns.
            if (s.TargetList is not { Count: 6 })
                return false;

            // Column positions 0-4 differ between Power BI versions (e.g. FK_TABLE_SCHEMA vs PK_TABLE_SCHEMA
            // at position 0).  The structural signature above is already conservative; we only check that the
            // final column is the FK_NAME expression, which is consistent across all known variants.
            return TryMatchFkNameTarget(s.TargetList[5]?.ResTarget);
        }

        /// <summary>
        /// Returns true if any node in the FROM clause of <paramref name="s"/> is a subquery
        /// (RangeSubselect) whose own FROM references the given schema.table.  Walks JOIN trees.
        /// </summary>
        private static bool ContainsSubqueryReferencingTable(SelectStmt s, string schema, string table)
        {
            if (s?.FromClause == null)
                return false;

            foreach (var from in s.FromClause)
            {
                if (NodeContainsSubqueryReferencingTable(from, schema, table))
                    return true;
            }

            return false;
        }

        private static bool NodeContainsSubqueryReferencingTable(Node node, string schema, string table)
        {
            if (node == null)
                return false;

            // If this node is a subquery, inspect its inner SELECT for the target table.
            var rss = node.RangeSubselect;
            if (rss?.Subquery?.SelectStmt != null)
            {
                if (ContainsFromTable(rss.Subquery.SelectStmt, schema, table))
                    return true;
            }

            // Recurse into JOIN tree.
            var join = node.JoinExpr;
            if (join != null)
            {
                if (NodeContainsSubqueryReferencingTable(join.Larg, schema, table))
                    return true;
                if (NodeContainsSubqueryReferencingTable(join.Rarg, schema, table))
                    return true;
            }

            return false;
        }

        private static bool TryGetFromTable(Node fromNode, out string schema, out string table)
        {
            schema = null;
            table = null;

            var rangeVar = fromNode?.RangeVar;
            if (rangeVar == null)
                return false;

            schema = rangeVar.Schemaname;
            table = rangeVar.Relname;
            return string.IsNullOrWhiteSpace(table) == false;
        }

        private static bool IsInformationSchema(string schema)
        {
            return string.Equals(schema, "information_schema", StringComparison.OrdinalIgnoreCase);
        }

    }
}
