using System;
using System.Collections.Generic;
using System.Globalization;
using PgSqlParser;
using Sparrow.Extensions;

namespace Raven.Server.Integrations.PostgreSQL.Translation
{
    /// <summary>
    /// Shared intermediate representation for a translated SQL WHERE clause.
    /// The IR is produced by <see cref="SqlWhereParser.TryParse"/> and consumed by
    /// emitters in <see cref="PgSqlToRqlTranslator"/> (general SQL→RQL flow) and
    /// <see cref="PowerBI.PowerBIOuterWhereTranslator"/> (PowerBI wrapper merging).
    /// One parser, two small emitters – no duplicated AST navigation.
    /// </summary>
    internal abstract record ParsedWhere;

    internal sealed record ParsedAnd(IReadOnlyList<ParsedWhere> Children) : ParsedWhere;
    internal sealed record ParsedOr(IReadOnlyList<ParsedWhere> Children) : ParsedWhere;
    internal sealed record ParsedNot(ParsedWhere Child) : ParsedWhere;

    /// <summary>Binary comparison: <c>field OP value</c>. <paramref name="Operator"/> is a canonical SQL token: =, !=, &lt;, &lt;=, &gt;, &gt;=.</summary>
    internal sealed record ParsedBinary(IReadOnlyList<string> FieldPath, string Operator, ParsedValue Value) : ParsedWhere;
    internal sealed record ParsedIn(IReadOnlyList<string> FieldPath, IReadOnlyList<ParsedValue> Values, bool Negated) : ParsedWhere;
    internal sealed record ParsedBetween(IReadOnlyList<string> FieldPath, ParsedValue Lower, ParsedValue Upper) : ParsedWhere;
    internal sealed record ParsedIsNull(IReadOnlyList<string> FieldPath, bool Negated) : ParsedWhere;

    internal enum ParsedValueKind { String, Long, Double, Bool, Null, Timestamp }

    /// <summary>
    /// Scalar value extracted from a SQL constant node. <see cref="Raw"/> carries the .NET representation
    /// (string / long / double / bool / DateTime-formatted string for Timestamp / null).
    /// </summary>
    internal sealed record ParsedValue(object Raw, ParsedValueKind Kind);

    internal static class SqlWhereParser
    {
        /// <summary>
        /// Parses a pgsqlparser WHERE clause <see cref="Node"/> tree into the shared IR.
        /// <paramref name="outerAliasToStrip"/> is the SQL-level alias (if any) that should be
        /// peeled off qualified column references. Callers add back any inner-query alias themselves.
        /// Identifier casing follows PostgreSQL semantics: unquoted identifiers are folded to lowercase
        /// by pgsqlparser (SQL standard); quoted identifiers preserve case via <c>Sval</c>. See
        /// libpg_query issue #59 for upstream background.
        /// Returns <c>false</c> if any subexpression cannot be represented in the IR – in which case
        /// <paramref name="result"/> is <c>null</c> and callers should decline the whole translation.
        /// </summary>
        public static bool TryParse(Node whereNode, string outerAliasToStrip, out ParsedWhere result)
        {
            result = null;
            if (whereNode == null)
                return false;

            if (whereNode.BoolExpr != null)
                return TryParseBoolExpr(whereNode.BoolExpr, outerAliasToStrip, out result);

            if (whereNode.AExpr != null)
                return TryParseAExpr(whereNode.AExpr, outerAliasToStrip, out result);

            if (whereNode.NullTest != null)
                return TryParseNullTest(whereNode.NullTest, outerAliasToStrip, out result);

            return false;
        }

        private static bool TryParseBoolExpr(BoolExpr boolExpr, string outerAliasToStrip, out ParsedWhere result)
        {
            result = null;
            if (boolExpr?.Args == null || boolExpr.Args.Count == 0)
                return false;

            switch (boolExpr.Boolop)
            {
                case BoolExprType.AndExpr:
                case BoolExprType.OrExpr:
                {
                    var children = new List<ParsedWhere>(boolExpr.Args.Count);
                    foreach (var arg in boolExpr.Args)
                    {
                        if (TryParse(arg, outerAliasToStrip, out var child) == false)
                            return false;
                        children.Add(child);
                    }

                    result = boolExpr.Boolop == BoolExprType.AndExpr
                        ? new ParsedAnd(children)
                        : new ParsedOr(children);
                    return true;
                }

                case BoolExprType.NotExpr:
                {
                    if (boolExpr.Args.Count != 1)
                        return false;

                    if (TryParse(boolExpr.Args[0], outerAliasToStrip, out var child) == false)
                        return false;

                    result = new ParsedNot(child);
                    return true;
                }

                default:
                    return false;
            }
        }

        private static bool TryParseAExpr(A_Expr aExpr, string outerAliasToStrip, out ParsedWhere result)
        {
            result = null;

            if (IsAExprKind(aExpr, A_Expr_Kind.AexprBetween))
            {
                if (TryExtractFieldPath(aExpr.Lexpr, outerAliasToStrip, out var field) == false)
                    return false;

                var items = aExpr.Rexpr?.List?.Items;
                if (items == null || items.Count != 2)
                    return false;

                if (TryExtractScalar(items[0], out var lower) == false)
                    return false;
                if (TryExtractScalar(items[1], out var upper) == false)
                    return false;

                result = new ParsedBetween(field, lower, upper);
                return true;
            }

            if (IsAExprKind(aExpr, A_Expr_Kind.AexprIn))
            {
                if (TryExtractFieldPath(aExpr.Lexpr, outerAliasToStrip, out var field) == false)
                    return false;

                if (TryExtractScalarList(aExpr.Rexpr, out var values) == false)
                    return false;

                var negated = TryGetBinaryOp(aExpr, out var opToken) && opToken == "<>";
                result = new ParsedIn(field, values, negated);
                return true;
            }

            if (TryGetBinaryOp(aExpr, out var op) == false)
                return false;

            if (IsKnownBinaryOp(op) == false)
                return false;

            if (TryExtractFieldPath(aExpr.Lexpr, outerAliasToStrip, out var leftField) == false)
                return false;

            if (TryExtractScalar(aExpr.Rexpr, out var rightValue) == false)
                return false;

            result = new ParsedBinary(leftField, op, rightValue);
            return true;
        }

        private static bool TryParseNullTest(NullTest nullTest, string outerAliasToStrip, out ParsedWhere result)
        {
            result = null;
            if (nullTest?.Arg == null)
                return false;

            if (TryExtractFieldPath(nullTest.Arg, outerAliasToStrip, out var field) == false)
                return false;

            result = nullTest.Nulltesttype switch
            {
                NullTestType.IsNull    => new ParsedIsNull(field, Negated: false),
                NullTestType.IsNotNull => new ParsedIsNull(field, Negated: true),
                _                      => null
            };
            return result != null;
        }

        /// <summary>
        /// Extracts the dotted field-path segments from a <see cref="ColumnRef"/> node using
        /// pgsqlparser's <c>Sval</c> values. If <paramref name="outerAliasToStrip"/> matches the
        /// first segment (case-insensitive), it is peeled off.
        /// Unquoted identifiers are lowercase (PostgreSQL standard); quoted identifiers preserve case.
        /// </summary>
        private static bool TryExtractFieldPath(Node node, string outerAliasToStrip, out IReadOnlyList<string> path)
        {
            path = null;
            var fields = node?.ColumnRef?.Fields;
            if (fields == null || fields.Count == 0)
                return false;

            var segments = new List<string>(fields.Count);
            foreach (var f in fields)
            {
                var s = f?.String?.Sval;
                if (string.IsNullOrWhiteSpace(s))
                    return false;
                segments.Add(s);
            }

            if (segments.Count > 1 &&
                string.IsNullOrWhiteSpace(outerAliasToStrip) == false &&
                string.Equals(segments[0], outerAliasToStrip, StringComparison.OrdinalIgnoreCase))
            {
                segments.RemoveAt(0);
            }

            if (segments.Count == 0)
                return false;

            path = segments;
            return true;
        }

        private static bool TryExtractScalar(Node node, out ParsedValue value)
        {
            value = null;
            if (node == null)
                return false;

            if (TryExtractTimestampLiteral(node, out value))
                return true;

            var c = node.AConst;
            if (c == null)
                return false;

            if (c.Sval != null && string.IsNullOrEmpty(c.Sval.Sval) == false)
            {
                value = new ParsedValue(c.Sval.Sval, ParsedValueKind.String);
                return true;
            }

            if (c.Ival != null)
            {
                value = new ParsedValue((long)c.Ival.Ival, ParsedValueKind.Long);
                return true;
            }

            if (c.Fval != null && string.IsNullOrEmpty(c.Fval.Fval) == false)
            {
                // pgsqlparser serialises floats as strings. Keep the raw string; emitters decide how to parse.
                if (double.TryParse(c.Fval.Fval, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    value = new ParsedValue(d, ParsedValueKind.Double);
                else
                    value = new ParsedValue(c.Fval.Fval, ParsedValueKind.Double);
                return true;
            }

            if (c.Boolval != null)
            {
                value = new ParsedValue(c.Boolval.Boolval, ParsedValueKind.Bool);
                return true;
            }

            return false;
        }

        private static bool TryExtractScalarList(Node node, out IReadOnlyList<ParsedValue> values)
        {
            values = null;
            var items = node?.List?.Items;
            if (items == null || items.Count == 0)
                return false;

            if (items.Count == 1 && items[0]?.List?.Items != null)
                items = items[0].List.Items;

            var result = new List<ParsedValue>(items.Count);
            foreach (var item in items)
            {
                if (TryExtractScalar(item, out var v) == false)
                    return false;
                result.Add(v);
            }

            if (result.Count == 0)
                return false;

            values = result;
            return true;
        }

        private static bool TryGetBinaryOp(A_Expr aExpr, out string op)
        {
            op = null;
            if (aExpr?.Name == null || aExpr.Name.Count != 1)
                return false;

            var s = aExpr.Name[0]?.String?.Sval;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            op = s.Trim();
            return true;
        }

        private static bool IsKnownBinaryOp(string op) =>
            op is "=" or "!=" or "<>" or "<" or "<=" or ">" or ">=";

        private static bool IsAExprKind(A_Expr expr, A_Expr_Kind kind) =>
            expr?.Kind == kind;

        private static bool TryExtractTimestampLiteral(Node node, out ParsedValue value)
        {
            value = null;
            var typeCast = node?.TypeCast;
            if (typeCast == null)
                return false;

            var names = typeCast.TypeName?.Names;
            if (names == null)
                return false;

            var hasTimestamp = false;
            foreach (var nameNode in names)
            {
                if (string.Equals(nameNode?.String?.Sval, "timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    hasTimestamp = true;
                    break;
                }
            }

            if (hasTimestamp == false)
                return false;

            var raw = typeCast.Arg?.AConst?.Sval?.Sval;
            if (raw == null)
                return false;

            // Parse as Unspecified (no TZ shift) to match the original PowerBI translator behaviour.
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) == false &&
                DateTime.TryParse(raw, out dt) == false)
                return false;

            value = new ParsedValue(dt.GetDefaultRavenFormat(), ParsedValueKind.Timestamp);
            return true;
        }
    }
}
