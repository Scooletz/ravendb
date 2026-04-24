using System;
using System.Collections.Generic;
using PgSqlParser;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    /// <summary>
    /// Structural predicates over a parsed <see cref="SelectStmt"/>. Classifiers compose these
    /// to answer "does this query touch table X?", "what does it project?", etc.
    /// </summary>
    internal static class SelectStmtShape
    {
        /// <summary>
        /// Parses <paramref name="queryText"/> and returns the single top-level SELECT when the
        /// input is a valid single-statement SELECT. Used as the first step in most classifiers.
        /// </summary>
        public static bool TryParseSingleSelect(string queryText, out SelectStmt selectStmt)
        {
            selectStmt = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            var parseResult = Parser.Parse(queryText);
            if (parseResult.IsSuccess == false || parseResult.Value?.Stmts is not { Count: 1 })
                return false;

            selectStmt = parseResult.Value.Stmts[0]?.Stmt?.SelectStmt;
            return selectStmt != null;
        }

        /// <summary>
        /// Parses <paramref name="queryText"/> and returns the top-level SELECT statements
        /// when the input is a multi-statement SELECT batch (1 or more). Returns false on
        /// parse failure, or when any statement is not a SELECT.
        /// </summary>
        public static bool TryParseSelectStatements(string queryText, out IReadOnlyList<SelectStmt> selects)
        {
            selects = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            var parseResult = Parser.Parse(queryText);
            if (parseResult.IsSuccess == false || parseResult.Value?.Stmts == null)
                return false;

            var stmts = parseResult.Value.Stmts;
            if (stmts.Count == 0)
                return false;

            var result = new SelectStmt[stmts.Count];
            for (int i = 0; i < stmts.Count; i++)
            {
                var s = stmts[i]?.Stmt?.SelectStmt;
                if (s == null)
                    return false;
                result[i] = s;
            }

            selects = result;
            return true;
        }

        /// <summary>
        /// Returns true if the SELECT's FROM clause is absent or empty — i.e. the target list
        /// is a pure expression like <c>SELECT version()</c>.
        /// </summary>
        public static bool HasNoFromClause(SelectStmt s)
            => s?.FromClause is not { Count: > 0 };

        /// <summary>
        /// Returns true if the SELECT projects exactly one target that is an unqualified
        /// function call with the given name and argument count. Matches <c>SELECT version()</c>,
        /// <c>SELECT current_setting('max_index_keys')</c>, etc.
        /// </summary>
        public static bool IsSingleUnqualifiedFunctionCall(SelectStmt s, string expectedName, int expectedArgCount, out FuncCall funcCall)
        {
            funcCall = null;

            if (s?.TargetList is not { Count: 1 })
                return false;

            funcCall = s.TargetList[0]?.ResTarget?.Val?.FuncCall;
            if (funcCall == null)
                return false;

            if (funcCall.Funcname is not { Count: 1 })
                return false;

            var name = funcCall.Funcname[0].String?.Sval;
            if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            return (funcCall.Args?.Count ?? 0) == expectedArgCount;
        }

        /// <summary>
        /// Walks the FROM clause (including JOIN arms) and returns true if any RangeVar
        /// matches <paramref name="tableName"/> (case-insensitive, schema ignored).
        /// Does not descend into subqueries — see <see cref="SubqueryReferencesTable"/>.
        /// </summary>
        public static bool ReferencesTable(SelectStmt s, string tableName)
        {
            if (s?.FromClause == null)
                return false;

            foreach (var from in s.FromClause)
            {
                if (NodeReferencesTable(from, schema: null, tableName))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Walks the FROM clause (including JOIN arms) and returns true if any RangeVar
        /// matches the given schema+table (both case-insensitive).
        /// </summary>
        public static bool ReferencesTable(SelectStmt s, string schema, string tableName)
        {
            if (s?.FromClause == null)
                return false;

            foreach (var from in s.FromClause)
            {
                if (NodeReferencesTable(from, schema, tableName))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if any RangeSubselect beneath the FROM clause (including JOIN arms)
        /// has an inner SELECT that references <paramref name="schema"/>.<paramref name="tableName"/>.
        /// </summary>
        public static bool SubqueryReferencesTable(SelectStmt s, string schema, string tableName)
        {
            if (s?.FromClause == null)
                return false;

            foreach (var from in s.FromClause)
            {
                if (NodeContainsSubqueryReferencingTable(from, schema, tableName))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if any FROM-clause node is a RangeSubselect (direct or under JOIN arms).
        /// Used to distinguish "has a nested SELECT as a FROM source" queries (Npgsql modern nested).
        /// </summary>
        public static bool ContainsSubselectInFrom(SelectStmt s)
        {
            if (s?.FromClause == null)
                return false;

            foreach (var node in s.FromClause)
            {
                if (NodeContainsSubselect(node))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if any target projects a <c>*</c> (star) — including qualified wildcards
        /// like <c>typ.*</c>. Used by the modern-nested type-catalog classifier.
        /// </summary>
        public static bool HasWildcardTarget(SelectStmt s)
        {
            if (s?.TargetList == null)
                return false;

            foreach (var target in s.TargetList)
            {
                var fields = target?.ResTarget?.Val?.ColumnRef?.Fields;
                if (fields == null)
                    continue;

                foreach (var field in fields)
                {
                    if (field?.AStar != null)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the set of names projected by the SELECT. Uses the explicit alias if present,
        /// otherwise the last segment of a qualified ColumnRef (<c>tbl.col</c> → <c>col</c>).
        /// Unaliased complex expressions contribute nothing.
        /// </summary>
        public static HashSet<string> CollectProjectedNames(SelectStmt s)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (s?.TargetList == null)
                return names;

            foreach (var target in s.TargetList)
            {
                var rt = target?.ResTarget;
                if (rt == null)
                    continue;

                if (string.IsNullOrWhiteSpace(rt.Name) == false)
                {
                    names.Add(rt.Name);
                    continue;
                }

                var fields = rt.Val?.ColumnRef?.Fields;
                if (fields is { Count: > 0 })
                {
                    var lastName = fields[^1]?.String?.Sval;
                    if (string.IsNullOrWhiteSpace(lastName) == false)
                        names.Add(lastName);
                }
            }

            return names;
        }

        /// <summary>
        /// Returns true iff every name in <paramref name="required"/> is present in the SELECT's
        /// projected-name set (<see cref="CollectProjectedNames"/>). Order does not matter.
        /// </summary>
        public static bool ProjectedNamesContainAll(SelectStmt s, params string[] required)
        {
            var names = CollectProjectedNames(s);
            foreach (var r in required)
            {
                if (names.Contains(r) == false)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true iff the SELECT's projected-name set is exactly <paramref name="expected"/>
        /// (same size, all members present). Intended for intents where extra projected columns
        /// would signal a different semantic meaning.
        /// </summary>
        public static bool ProjectedNamesEqual(SelectStmt s, params string[] expected)
        {
            var names = CollectProjectedNames(s);
            if (names.Count != expected.Length)
                return false;

            foreach (var e in expected)
            {
                if (names.Contains(e) == false)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true if any target in the SELECT projects <paramref name="expectedName"/>
        /// (via explicit alias or last-segment column reference).
        /// </summary>
        public static bool ProjectsName(SelectStmt s, string expectedName)
        {
            var names = CollectProjectedNames(s);
            return names.Contains(expectedName);
        }

        // ── Structural helpers for the type-catalog C/D discriminator ─────────────────────

        /// <summary>
        /// Attempts to locate the inner OR node inside the
        /// <c>(proname='array_recv' AND &lt;inner-OR&gt;)</c> AND-branch of the top-level WHERE OR.
        /// Shared between TypeCatalog-OldFlat-WithPseudoArrays (asserts <c>'p'</c> present inside)
        /// and TypeCatalog-OldFlat-WithoutPseudoArrays (asserts <c>'p'</c> absent inside).
        /// </summary>
        public static bool TryGetArrayRecvInnerOrBlock(SelectStmt s, out Node innerOrNode)
        {
            innerOrNode = null;

            if (s?.WhereClause?.BoolExpr is not { Boolop: BoolExprType.OrExpr } topOr)
                return false;

            foreach (var arg in topOr.Args)
            {
                var andExpr = arg?.BoolExpr;
                if (andExpr is not { Boolop: BoolExprType.AndExpr })
                    continue;

                bool hasArrayRecvGuard = false;
                Node candidateInnerOr = null;

                foreach (var andArg in andExpr.Args)
                {
                    if (IsAExprRhsStringConstant(andArg, "array_recv"))
                        hasArrayRecvGuard = true;
                    else if (andArg?.BoolExpr is { Boolop: BoolExprType.OrExpr })
                        candidateInnerOr = andArg;
                }

                if (hasArrayRecvGuard && candidateInnerOr != null)
                {
                    innerOrNode = candidateInnerOr;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Recursive scan for any A_Expr whose direct RHS is a string constant equal to
        /// <paramref name="value"/>. Descends into BoolExpr args and AExpr Lexpr/Rexpr only —
        /// does not walk INTO list literals, so IN-list values are not inadvertently inspected.
        /// </summary>
        public static bool SubtreeContainsAExprRhsStringConstant(Node node, string value)
        {
            if (node == null)
                return false;

            if (IsAExprRhsStringConstant(node, value))
                return true;

            var be = node.BoolExpr;
            if (be != null)
            {
                foreach (var arg in be.Args)
                {
                    if (SubtreeContainsAExprRhsStringConstant(arg, value))
                        return true;
                }
            }

            var ae = node.AExpr;
            if (ae != null)
            {
                if (SubtreeContainsAExprRhsStringConstant(ae.Lexpr, value))
                    return true;
                if (SubtreeContainsAExprRhsStringConstant(ae.Rexpr, value))
                    return true;
            }

            return false;
        }

        // ── Private helpers ───────────────────────────────────────────────────────────────

        private static bool NodeReferencesTable(Node node, string schema, string tableName)
        {
            if (node == null)
                return false;

            var rv = node.RangeVar;
            if (rv != null &&
                string.Equals(rv.Relname, tableName, StringComparison.OrdinalIgnoreCase) &&
                (schema == null || string.Equals(rv.Schemaname, schema, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var join = node.JoinExpr;
            if (join != null)
            {
                if (NodeReferencesTable(join.Larg, schema, tableName))
                    return true;
                if (NodeReferencesTable(join.Rarg, schema, tableName))
                    return true;
            }

            return false;
        }

        private static bool NodeContainsSubselect(Node node)
        {
            if (node == null)
                return false;

            if (node.RangeSubselect != null)
                return true;

            var join = node.JoinExpr;
            if (join != null)
            {
                if (NodeContainsSubselect(join.Larg))
                    return true;
                if (NodeContainsSubselect(join.Rarg))
                    return true;
            }

            return false;
        }

        private static bool NodeContainsSubqueryReferencingTable(Node node, string schema, string tableName)
        {
            if (node == null)
                return false;

            var rss = node.RangeSubselect;
            if (rss?.Subquery?.SelectStmt != null)
            {
                if (ReferencesTable(rss.Subquery.SelectStmt, schema, tableName))
                    return true;
            }

            var join = node.JoinExpr;
            if (join != null)
            {
                if (NodeContainsSubqueryReferencingTable(join.Larg, schema, tableName))
                    return true;
                if (NodeContainsSubqueryReferencingTable(join.Rarg, schema, tableName))
                    return true;
            }

            return false;
        }

        private static bool IsAExprRhsStringConstant(Node node, string value)
        {
            var expr = node?.AExpr;
            return expr != null && expr.Rexpr?.AConst?.Sval?.Sval == value;
        }
    }
}
