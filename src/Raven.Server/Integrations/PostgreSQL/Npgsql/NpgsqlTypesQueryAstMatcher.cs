using System;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL.Npgsql
{
    /// <summary>
    /// AST-based matchers for the Npgsql pg_catalog type-loading query families.
    /// One method per family; families are added incrementally (plan: A → B → E → C → D).
    /// </summary>
    internal static class NpgsqlTypesQueryAstMatcher
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

            if (IsModernNestedTypesQuery(select))
            {
                // Npgsql5TypesResponse and Npgsql4TypesResponse are data-identical (confirmed by diff).
                result = NpgsqlConfig.Npgsql5TypesResponse;
                return true;
            }

            if (IsMidFlatTypesQuery(select))
            {
                result = NpgsqlConfig.Npgsql4_1_2TypesResponse;
                return true;
            }

            if (IsLegacyNpgsql3TypesQuery(select))
            {
                result = NpgsqlConfig.Npgsql3TypesResponse;
                return true;
            }

            if (IsOldFlatFamilyCTypesQuery(select))
            {
                // TypesResponse and Npgsql4_0_3TypesResponse are data-identical (confirmed by diff).
                result = NpgsqlConfig.TypesResponse;
                return true;
            }

            if (IsOldFlatFamilyDTypesQuery(select))
            {
                result = NpgsqlConfig.Npgsql4_0_0TypesResponse;
                return true;
            }

            return false;
        }

        // ── Family A — Modern nested (Npgsql 4.1.3–5.x+) ────────────────────────────────────────
        // Anchors (all required):
        //   1. FROM tree contains a RangeSubselect — no other Npgsql type query has a subquery in FROM.
        //   2. Outer SELECT has exactly 3 targets (nspname, typ_and_elem_type.*, CASE AS ord).
        //   3. One target is a wildcard .* projection (typ_and_elem_type.*).
        // The two variants (Npgsql4 / Npgsql5) differ only by a leading \n — identical ASTs.
        private static bool IsModernNestedTypesQuery(SelectStmt s)
        {
            if (s.TargetList is not { Count: 3 })
                return false;

            if (HasWildcardTarget(s) == false)
                return false;

            if (s.FromClause == null)
                return false;

            foreach (var node in s.FromClause)
            {
                if (NodeContainsSubselect(node))
                    return true;
            }

            return false;
        }

        // ── Family B — Mid flat (Npgsql 4.1.0–4.1.2) ────────────────────────────────────────────
        // Anchors (both required):
        //   1. Exactly 7 projected columns — unique count across all Npgsql type-loading families.
        //   2. `typelem` appears in the projected names — Family B aliases a CASE expression to
        //      `typelem`; other flat families use `elemoid` for the element-type column.
        private static bool IsMidFlatTypesQuery(SelectStmt s)
        {
            if (s.TargetList is not { Count: 7 })
                return false;

            return HasTargetNamed(s, "typelem");
        }

        // ── Family E — Legacy Npgsql 3 (3.2.3–3.2.7) ────────────────────────────────────────────
        // Anchors:
        //   1. Exactly 8 projected columns.
        //   2. `pg_type` and `pg_proc` present in FROM.
        //   3. `pg_class` absent from FROM — key discriminator; all later 8-col families join pg_class.
        //   4. Projected names match the expected Family E set (nspname, typname, oid, typrelid,
        //      typbasetype, type, elemoid, ord) — guards against unrelated pg_type/pg_proc queries.
        private static readonly string[] _familyEExpectedColumns =
        {
            "nspname", "typname", "oid", "typrelid", "typbasetype", "type", "elemoid", "ord"
        };

        private static bool IsLegacyNpgsql3TypesQuery(SelectStmt s)
        {
            if (s.TargetList is not { Count: 8 })
                return false;

            if (s.FromClause == null)
                return false;

            if (FromContainsTable(s, "pg_type") == false)
                return false;

            if (FromContainsTable(s, "pg_proc") == false)
                return false;

            // Key discriminator: Npgsql 3 never joined pg_class; all later 8-col families do.
            if (FromContainsTable(s, "pg_class"))
                return false;

            // Column-set check: guards against unrelated pg_type/pg_proc queries.
            foreach (var col in _familyEExpectedColumns)
            {
                if (HasTargetNamed(s, col) == false)
                    return false;
            }

            return true;
        }

        // ── Family C — Old flat with pseudo-type arrays (Npgsql 4.0.1–4.0.12) ─────────────────────
        // Anchors (all required):
        //   1. Exactly 8 projected columns.
        //   2. pg_type, pg_proc, pg_class all present in FROM.
        //   3. Projected names match the Family C/E column set (identical sets, confirmed).
        //   4. WHERE: the array_recv-gated OR block contains a branch whose RHS is 'p' — the
        //      pseudo-type condition (b.typtype='p') that Family D lacks.
        // TypesQuery and Npgsql4_0_3TypesQuery differ only by a leading \n → identical ASTs.
        private static bool IsOldFlatFamilyCTypesQuery(SelectStmt s)
        {
            if (s.TargetList is not { Count: 8 })
                return false;

            if (s.FromClause == null)
                return false;

            if (FromContainsTable(s, "pg_type") == false)
                return false;

            if (FromContainsTable(s, "pg_proc") == false)
                return false;

            // pg_class presence is the key FROM discriminator vs. Family E.
            if (FromContainsTable(s, "pg_class") == false)
                return false;

            // Column-set check (identical to Family E; both project the same 8 names).
            foreach (var col in _familyEExpectedColumns)
            {
                if (HasTargetNamed(s, col) == false)
                    return false;
            }

            // WHERE discriminator: Family C has a pseudo-type branch inside the array_recv
            // sub-expression (b.typtype='p'); Family D does not — this is the only C/D difference.
            return HasPseudoTypeBranchInArrayRecvBlock(s);
        }

        // ── Family D — Old flat without pseudo-type arrays (Npgsql 4.0.0 only) ─────────────────────
        // Anchors 1–5 are identical to Family C. Anchor 6 is a scoped absence check — NOT a plain
        // negation of the whole C matcher; the array_recv block must be positively located first.
        //   1. Exactly 8 projected columns.
        //   2. pg_type, pg_proc, pg_class all present in FROM.
        //   3. Projected names match the Family C/D/E column set.
        //   4–5. Top-level WHERE OR present; array_recv AND-block located within it.
        //   6.   That block does NOT contain an A_Expr with RHS 'p'.
        private static bool IsOldFlatFamilyDTypesQuery(SelectStmt s)
        {
            if (s.TargetList is not { Count: 8 })
                return false;

            if (s.FromClause == null)
                return false;

            if (FromContainsTable(s, "pg_type") == false)
                return false;

            if (FromContainsTable(s, "pg_proc") == false)
                return false;

            if (FromContainsTable(s, "pg_class") == false)
                return false;

            foreach (var col in _familyEExpectedColumns)
            {
                if (HasTargetNamed(s, col) == false)
                    return false;
            }

            // Positive anchor: block must exist. Absence anchor: 'p' must not be inside it.
            return TryGetArrayRecvInnerOrBlock(s, out var innerOr)
                && SubtreeContainsAExprRhsStringConstant(innerOr, "p") == false;
        }

        // Locates the inner OR node inside the (proname='array_recv' AND <inner-OR>) AND-branch
        // of the top-level WHERE OR. Shared by Family C (asserts 'p' present) and
        // Family D (asserts 'p' absent).
        private static bool TryGetArrayRecvInnerOrBlock(SelectStmt s, out Node innerOrNode)
        {
            innerOrNode = null;

            if (s.WhereClause?.BoolExpr is not { Boolop: BoolExprType.OrExpr } topOr)
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

        // Family C WHERE discriminator: array_recv block present AND 'p' branch found inside it.
        private static bool HasPseudoTypeBranchInArrayRecvBlock(SelectStmt s)
            => TryGetArrayRecvInnerOrBlock(s, out var innerOr)
               && SubtreeContainsAExprRhsStringConstant(innerOr, "p");

        // Returns true if node is an A_Expr of any kind whose direct Rexpr is the string constant value.
        private static bool IsAExprRhsStringConstant(Node node, string value)
        {
            var expr = node?.AExpr;
            return expr != null && expr.Rexpr?.AConst?.Sval?.Sval == value;
        }

        // Recursively scans a node subtree for any A_Expr whose direct RHS string constant equals value.
        // Only descends into BoolExpr args and AExpr Lexpr/Rexpr — does not walk INTO list literals,
        // so IN-list values (stored in List nodes off Rexpr) are not inadvertently inspected.
        private static bool SubtreeContainsAExprRhsStringConstant(Node node, string value)
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

        // ── Shared helpers ────────────────────────────────────────────────────────────────────────

        private static bool HasWildcardTarget(SelectStmt s)
        {
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

        // Returns true if any target in the SELECT list resolves to the given name
        // (explicit alias takes precedence; falls back to the last field of a ColumnRef).
        private static bool HasTargetNamed(SelectStmt s, string name)
        {
            foreach (var node in s.TargetList)
            {
                var rt = node?.ResTarget;
                if (rt == null)
                    continue;

                if (string.IsNullOrWhiteSpace(rt.Name) == false)
                {
                    if (string.Equals(rt.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    var fields = rt.Val?.ColumnRef?.Fields;
                    if (fields is { Count: > 0 })
                    {
                        var colName = fields[^1]?.String?.Sval;
                        if (string.Equals(colName, name, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
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

        private static bool FromContainsTable(SelectStmt s, string tableName)
        {
            foreach (var node in s.FromClause)
            {
                if (NodeContainsTable(node, tableName))
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
