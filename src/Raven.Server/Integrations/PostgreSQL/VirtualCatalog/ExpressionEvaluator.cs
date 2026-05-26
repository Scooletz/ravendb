using System.Collections.Generic;
using System.Globalization;
using PgSqlParser;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog
{
    // Evaluates a value expression (a parser Node) against a RowScope, returning the cell value
    // as an object. Mirror of PredicateEvaluator but on the raw AST — projection targets and
    // CASE WHEN result clauses don't have a ParsedWhere IR.
    //
    // Scope: AConst literals, ColumnRef, CaseExpr (sequential WHENs, ELSE result), NullTest,
    // BoolExpr/AExpr boolean expressions (evaluate to a bool object), and a narrow FuncCall slice
    // (only literal-name proname checks via array_recv handling appear in the target queries, and
    // those are surfaced as ColumnRef on a join scope rather than a function call here).
    internal static class ExpressionEvaluator
    {
        public static bool TryEvaluate(Node node, RowScope scope, out object value)
        {
            value = null;
            if (node == null)
                return false;

            if (node.AConst != null)
                return TryEvaluateConst(node.AConst, out value);

            if (node.ColumnRef != null)
                return TryEvaluateColumnRef(node.ColumnRef, scope, out value);

            if (node.CaseExpr != null)
                return TryEvaluateCase(node.CaseExpr, scope, out value);

            if (node.NullTest != null)
                return TryEvaluateNullTest(node.NullTest, scope, out value);

            if (node.BoolExpr != null)
                return TryEvaluateBoolExpr(node.BoolExpr, scope, out value);

            if (node.AExpr != null)
                return TryEvaluateAExpr(node.AExpr, scope, out value);

            if (node.TypeCast != null)
                return TryEvaluate(node.TypeCast.Arg, scope, out value);

            if (node.RelabelType != null)
                return TryEvaluate(node.RelabelType.Arg, scope, out value);

            return false;
        }

        private static bool TryEvaluateConst(A_Const c, out object value)
        {
            value = null;

            if (c.Isnull)
                return true;

            if (c.Sval != null && c.Sval.Sval != null)
            {
                value = c.Sval.Sval;
                return true;
            }
            if (c.Ival != null)
            {
                value = (long)c.Ival.Ival;
                return true;
            }
            if (c.Fval != null && string.IsNullOrEmpty(c.Fval.Fval) == false)
            {
                if (double.TryParse(c.Fval.Fval, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    value = d;
                else
                    value = c.Fval.Fval;
                return true;
            }
            if (c.Boolval != null)
            {
                value = c.Boolval.Boolval;
                return true;
            }

            return false;
        }

        private static bool TryEvaluateColumnRef(ColumnRef colRef, RowScope scope, out object value)
        {
            value = null;
            if (colRef.Fields is not { Count: > 0 } fields)
                return false;

            var path = new List<string>(fields.Count);
            foreach (var f in fields)
            {
                var s = f?.String?.Sval;
                if (string.IsNullOrWhiteSpace(s))
                    return false;
                path.Add(s);
            }

            return scope.TryLookup(path, out value);
        }

        private static bool TryEvaluateCase(CaseExpr caseExpr, RowScope scope, out object value)
        {
            value = null;
            if (caseExpr.Args is not { Count: > 0 } whens)
                return false;

            foreach (var whenNode in whens)
            {
                var when = whenNode?.CaseWhen;
                if (when == null)
                    return false;

                if (TryEvaluate(when.Expr, scope, out var cond) == false)
                    return false;

                if (IsTruthy(cond))
                {
                    return TryEvaluate(when.Result, scope, out value);
                }
            }

            if (caseExpr.Defresult != null)
                return TryEvaluate(caseExpr.Defresult, scope, out value);

            // No WHEN matched and no ELSE → SQL NULL.
            value = null;
            return true;
        }

        private static bool TryEvaluateNullTest(NullTest nullTest, RowScope scope, out object value)
        {
            value = null;
            if (TryEvaluate(nullTest.Arg, scope, out var inner) == false)
                return false;

            var isNull = inner is null;
            value = nullTest.Nulltesttype switch
            {
                NullTestType.IsNull    => isNull,
                NullTestType.IsNotNull => isNull == false,
                _ => (object)null
            };
            return value != null;
        }

        private static bool TryEvaluateBoolExpr(BoolExpr boolExpr, RowScope scope, out object value)
        {
            value = null;
            if (boolExpr?.Args is not { Count: > 0 } args)
                return false;

            switch (boolExpr.Boolop)
            {
                case BoolExprType.AndExpr:
                    foreach (var arg in args)
                    {
                        if (TryEvaluate(arg, scope, out var childAnd) == false)
                            return false;
                        if (IsTruthy(childAnd) == false)
                        {
                            value = false;
                            return true;
                        }
                    }
                    value = true;
                    return true;

                case BoolExprType.OrExpr:
                    foreach (var arg in args)
                    {
                        if (TryEvaluate(arg, scope, out var childOr) == false)
                            return false;
                        if (IsTruthy(childOr))
                        {
                            value = true;
                            return true;
                        }
                    }
                    value = false;
                    return true;

                case BoolExprType.NotExpr:
                    if (args.Count != 1)
                        return false;
                    if (TryEvaluate(args[0], scope, out var inner) == false)
                        return false;
                    value = IsTruthy(inner) == false;
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryEvaluateAExpr(A_Expr aExpr, RowScope scope, out object value)
        {
            value = null;

            if (aExpr.Kind == A_Expr_Kind.AexprIn)
                return TryEvaluateInExpr(aExpr, scope, out value);

            if (aExpr.Name is not { Count: 1 })
                return false;

            var op = aExpr.Name[0]?.String?.Sval;
            if (string.IsNullOrEmpty(op))
                return false;

            if (TryEvaluate(aExpr.Lexpr, scope, out var lhs) == false)
                return false;
            if (TryEvaluate(aExpr.Rexpr, scope, out var rhs) == false)
                return false;

            var cmp = CompareValues(lhs, rhs);
            if (cmp == null && IsEqualityOp(op) == false)
                return false;

            value = op switch
            {
                "="  => cmp.HasValue && cmp.Value == 0,
                "!=" => cmp.HasValue == false || cmp.Value != 0,
                "<>" => cmp.HasValue == false || cmp.Value != 0,
                "<"  => cmp.HasValue && cmp.Value < 0,
                "<=" => cmp.HasValue && cmp.Value <= 0,
                ">"  => cmp.HasValue && cmp.Value > 0,
                ">=" => cmp.HasValue && cmp.Value >= 0,
                _ => (object)null
            };
            return value != null;
        }

        private static bool TryEvaluateInExpr(A_Expr aExpr, RowScope scope, out object value)
        {
            value = null;
            if (TryEvaluate(aExpr.Lexpr, scope, out var lhs) == false)
                return false;

            var items = aExpr.Rexpr?.List?.Items;
            if (items == null)
                return false;
            if (items.Count == 1 && items[0]?.List?.Items != null)
                items = items[0].List.Items;

            var negated = false;
            if (aExpr.Name is { Count: 1 } && aExpr.Name[0]?.String?.Sval == "<>")
                negated = true;

            foreach (var item in items)
            {
                if (TryEvaluate(item, scope, out var candidate) == false)
                    return false;
                var cmp = CompareValues(lhs, candidate);
                if (cmp == 0)
                {
                    value = negated == false;
                    return true;
                }
            }

            value = negated;
            return true;
        }

        // Comparison semantics used by both = / <> / < and IN. Null on either side → null result
        // (matches SQL three-valued logic; callers fall through to the next CASE WHEN).
        private static int? CompareValues(object lhs, object rhs)
        {
            if (lhs is null || rhs is null)
                return null;

            if (lhs is bool lb && rhs is bool rb)
                return lb.CompareTo(rb);

            if (TryToLong(lhs, out var ll) && TryToLong(rhs, out var rl))
                return ll.CompareTo(rl);

            if (TryToDouble(lhs, out var ld) && TryToDouble(rhs, out var rd))
                return ld.CompareTo(rd);

            var ls = lhs.ToString();
            var rs = rhs.ToString();
            return string.CompareOrdinal(ls, rs);
        }

        private static bool TryToLong(object v, out long result)
        {
            switch (v)
            {
                case long l: result = l; return true;
                case int i: result = i; return true;
                case short s: result = s; return true;
                case byte b: result = b; return true;
                case string str when long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p):
                    result = p; return true;
                default:
                    result = 0; return false;
            }
        }

        private static bool TryToDouble(object v, out double result)
        {
            switch (v)
            {
                case double d: result = d; return true;
                case float f: result = f; return true;
                case long l: result = l; return true;
                case int i: result = i; return true;
                case string str when double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var p):
                    result = p; return true;
                default:
                    result = 0; return false;
            }
        }

        private static bool IsEqualityOp(string op) => op is "=" or "!=" or "<>";

        public static bool IsTruthy(object value)
            => value switch
            {
                null => false,
                bool b => b,
                _ => true,
            };
    }
}
