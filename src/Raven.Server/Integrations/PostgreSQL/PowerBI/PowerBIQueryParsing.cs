using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using PgSqlParser;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Queries.AST;
using Raven.Server.Documents.Queries.Parser;
using RavenQuery = Raven.Server.Documents.Queries.AST.Query;
using Raven.Server.Integrations.PostgreSQL.Translation;
using Sparrow;
using Sparrow.Extensions;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    /// <summary>
    /// Result of <see cref="PowerBIInnerRqlExtractor.TryExtractAndResolve"/>.
    /// <see cref="InnerText"/> is the extracted innermost query text (RQL or SQL).
    /// <see cref="SanitizedSql"/> is the outer wrapper SQL with the innermost query replaced by
    /// a trivial <c>select 1</c> subquery so pgsqlparser can safely parse the wrapper structure.
    /// </summary>
    internal sealed record InnerTextResult(
        string InnerText,
        string SanitizedSql,
        RavenQuery ResolvedQuery);

    internal static class PowerBIInnerRqlExtractor
    {
        /// <summary>
        /// Extracts the inner textbox span from a PowerBI-wrapped SQL query and resolves it to a
        /// <see cref="RavenQuery"/> in one step. Returns <c>null</c> when extraction or resolution fails.
        /// </summary>
        public static InnerTextResult TryExtractAndResolve(string sql)
        {
            if (TryExtractInnerText(sql, out var innerText, out var sanitizedSql, out var fromTwoParsersPath) == false)
                return null;

            var resolved = TryResolveInnerTextToQuery(innerText, fromTwoParsersPath);
            if (resolved == null)
                return null;

            return new InnerTextResult(innerText, sanitizedSql, resolved);
        }

        private static bool TryExtractInnerText(string sql, out string innerText, out string sanitizedSql, out bool fromTwoParsersPath)
        {
            innerText = null;
            sanitizedSql = null;
            fromTwoParsersPath = false;

            if (TryExtractInnerRqlSpanViaTwoParsers(sql, out var innerStart, out var innerEnd, out innerText))
            {
                fromTwoParsersPath = true;
                sanitizedSql = sql[..innerStart] + "select 1" + sql[innerEnd..];
                return true;
            }

            return TryExtractInnerSqlViaAst(sql, out innerText, out sanitizedSql);
        }

        private static RavenQuery TryResolveInnerTextToQuery(string innerText, bool fromTwoParsersPath)
        {
            if (string.IsNullOrWhiteSpace(innerText))
                return null;

            if (fromTwoParsersPath)
            {
                // Preferred two-parsers path confirmed embedded RQL – parse as RQL only.
                try
                {
                    return QueryMetadata.ParseQuery(innerText, QueryType.Select);
                }
                catch
                {
                    return null;
                }
            }

            // Preferred path did not succeed; treat the extracted text as ambiguous.

            // 1. Try RQL first (preserves correct behaviour when the inner content is RQL
            //    despite the two-parsers path not firing).
            try
            {
                return QueryMetadata.ParseQuery(innerText, QueryType.Select);
            }
            catch
            {
                // Not RQL – fall through to SQL translation.
            }

            // 2. Try SQL→RQL translation (SQL-statement textbox support).
            // PgSqlToRqlTranslator handles the SELECT…FROM…WHERE shape that the user types in the
            // textbox. The global-fallback translator in PgQuery.CreateInstance is intentionally
            // not reused here to avoid false positives.
            if (PgSqlToRqlTranslator.TryParse(innerText, parameterTypes: Array.Empty<int>(), out var translatedRql) == false)
                return null;

            try
            {
                return QueryMetadata.ParseQuery(translatedRql, QueryType.Select);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryExtractInnerRqlSpanViaTwoParsers(string sql, out int innerStart, out int innerEnd, out string innerRql)
        {
            innerStart = 0;
            innerEnd = 0;
            innerRql = null;

            if (string.IsNullOrWhiteSpace(sql))
                return false;

            var parseResult = Parser.Parse(sql);

            if (parseResult.IsSuccess)
                return false;

            var cursorPos1Based = parseResult.Error?.CursorPos ?? 0;
            if (cursorPos1Based <= 0)
                return false;
            var start = cursorPos1Based - 1;
            if (TryNormalizeStartIndex(sql, start, out start) == false)
                return false;

            if (StartsWithKeywordAtWordBoundary(sql, start, "from") == false)
            {
                if (TryRecoverDeclareFunctionStart(sql, start, out start) == false)
                    return false;
            }

            if (TryParseRqlPrefixAndGetEnd(sql, start, out var end) == false)
                return false;

            if (TryConsumeNextNonWsChar(sql, end, ')', out var afterCloseParen) == false)
                return false;

            if (TryConsumeQuotedAlias(sql, afterCloseParen) == false)
                return false;

            innerStart = start;
            innerEnd = end;
            innerRql = sql.Substring(innerStart, innerEnd - innerStart).Trim();
            return true;
        }

        private static bool TryRecoverDeclareFunctionStart(string sql, int cursorStart, out int recoveredStart)
        {
            recoveredStart = 0;

            if (string.IsNullOrWhiteSpace(sql))
                return false;

            if ((uint)cursorStart >= (uint)sql.Length)
                return false;

            int i = cursorStart - 1;

            // Allow whitespace between the function name and the 'function' keyword.
            while (i >= 0 && char.IsWhiteSpace(sql[i]))
                i--;

            if (i < 0)
                return false;

            var functionStart = i - "function".Length + 1;
            if (functionStart < 0)
                return false;

            if (StartsWithKeywordAtWordBoundary(sql, functionStart, "function") == false)
                return false;

            i = functionStart - 1;

            // Allow whitespace between 'declare' and 'function'.
            while (i >= 0 && char.IsWhiteSpace(sql[i]))
                i--;

            if (i < 0)
                return false;

            var declareStart = i - "declare".Length + 1;
            if (declareStart < 0)
                return false;

            if (StartsWithKeywordAtWordBoundary(sql, declareStart, "declare") == false)
                return false;

            recoveredStart = declareStart;
            return true;
        }

        private static bool TryExtractInnerSqlViaAst(string sql, out string innerSql, out string sanitizedSql)
        {
            innerSql = null;
            sanitizedSql = null;

            if (string.IsNullOrWhiteSpace(sql))
                return false;

            var parseResult = Parser.Parse(sql);
            if (parseResult.IsSuccess == false || parseResult.Value?.Stmts is not { Count: 1 })
                return false;

            var rootSelect = parseResult.Value.Stmts[0]?.Stmt?.SelectStmt;
            if (rootSelect == null)
                return false;

            if (TryFindDeepestWrapperSubquery(rootSelect, out var wrapperSubqueryNode, out var innerSelect) == false)
                return false;

            if (TryDeparseSelect(innerSelect, out innerSql) == false)
                return false;

            wrapperSubqueryNode.Subquery = CreateSelect1Node();

            if (TryDeparseParseResult(parseResult.Value, out sanitizedSql) == false)
                return false;

            return string.IsNullOrWhiteSpace(innerSql) == false &&
                   string.IsNullOrWhiteSpace(sanitizedSql) == false;
        }

        private static bool TryFindDeepestWrapperSubquery(SelectStmt rootSelect, out RangeSubselect wrapperSubqueryNode, out SelectStmt innerSelect)
        {
            wrapperSubqueryNode = null;
            innerSelect = null;

            var current = rootSelect;
            var found = false;

            while (current != null)
            {
                if (current.FromClause is not { Count: 1 })
                    break;

                var rss = current.FromClause[0]?.RangeSubselect;
                if (rss == null)
                    break;

                if (IsPowerBiWrapperAlias(rss.Alias?.Aliasname) == false)
                    break;

                var next = rss.Subquery?.SelectStmt;
                if (next == null)
                    return false;

                wrapperSubqueryNode = rss;
                innerSelect = next;
                current = next;
                found = true;
            }

            return found;
        }

        private static bool IsPowerBiWrapperAlias(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return false;

            return string.Equals(alias, "$Table", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(alias, "_", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(alias, "rows", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryDeparseSelect(SelectStmt selectStmt, out string sql)
        {
            sql = null;

            if (selectStmt == null)
                return false;

            var parseResult = new ParseResult();
            parseResult.Stmts.Add(new RawStmt
            {
                Stmt = new Node
                {
                    SelectStmt = selectStmt
                }
            });

            return TryDeparseParseResult(parseResult, out sql);
        }

        private static bool TryDeparseParseResult(ParseResult parseResult, out string sql)
        {
            sql = null;

            if (parseResult == null)
                return false;

            var deparseResult = Parser.Deparse(parseResult);
            if (deparseResult.IsSuccess == false)
                return false;

            sql = deparseResult.Value;
            return string.IsNullOrWhiteSpace(sql) == false;
        }

        private static Node CreateSelect1Node()
        {
            var selectStmt = new SelectStmt();

            selectStmt.TargetList.Add(new Node
            {
                ResTarget = new ResTarget
                {
                    Val = new Node
                    {
                        AConst = new A_Const
                        {
                            Ival = new Integer
                            {
                                Ival = 1
                            }
                        }
                    }
                }
            });

            return new Node
            {
                SelectStmt = selectStmt
            };
        }

        private static bool TryNormalizeStartIndex(string s, int start, out int normalizedStart)
        {
            normalizedStart = 0;

            if ((uint)start >= (uint)s.Length)
                return false;

            start = SkipWhitespaceForward(s, start);
            if (start >= s.Length)
                return false;

            normalizedStart = NormalizeIdentifierStart(s, start);
            return true;
        }

        private static int SkipWhitespaceForward(string s, int i)
        {
            while ((uint)i < (uint)s.Length && char.IsWhiteSpace(s[i]))
                i++;
            return i;
        }

        private static int NormalizeIdentifierStart(string s, int i)
        {
            if ((uint)i >= (uint)s.Length)
                return i;

            if (IsIdentChar(s[i]) == false)
                return i;

            while (i > 0 && IsIdentChar(s[i - 1]))
                i--;

            return i;
        }

        private static bool StartsWithKeywordAtWordBoundary(string s, int i, string keyword)
        {
            if ((uint)i >= (uint)s.Length)
                return false;

            if (i > 0 && IsIdentChar(s[i - 1]))
                return false;

            if (s.AsSpan(i).StartsWith(keyword, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            var end = i + keyword.Length;
            if (end < s.Length && IsIdentChar(s[end]))
                return false;

            return true;
        }

        private static bool TryParseRqlPrefixAndGetEnd(string sql, int start, out int end)
        {
            end = 0;

            var slice = sql.Substring(start);
            var qp = new QueryParser();
            qp.Init(slice);

            if (qp.TryParse(out _, out _, QueryType.Select, recursive: true) == false)
                return false;

            var consumed = qp.Scanner.Position;
            if (consumed <= 0 || consumed > slice.Length)
                return false;

            end = start + consumed;
            return end > start && end <= sql.Length;
        }

        private static bool TryConsumeNextNonWsChar(string s, int i, char expected, out int nextIndex)
        {
            nextIndex = 0;

            var idx = SkipWhitespaceForward(s, i);
            if ((uint)idx >= (uint)s.Length)
                return false;

            if (s[idx] != expected)
                return false;

            nextIndex = idx + 1;
            return true;
        }

        private static bool TryConsumeQuotedAlias(string s, int i)
        {
            return TryReadQuotedAlias(s, i, out _, out _);
        }

        private static bool TryReadQuotedAlias(string s, int i, out string alias, out int nextIndex)
        {
            alias = null;
            nextIndex = 0;

            var idx = SkipWhitespaceForward(s, i);
            if ((uint)idx >= (uint)s.Length || s[idx] != '"')
                return false;

            var endQuote = s.IndexOf('"', idx + 1);
            if (endQuote == -1)
                return false;

            alias = s.Substring(idx + 1, endQuote - idx - 1);
            if (string.IsNullOrWhiteSpace(alias))
                return false;

            nextIndex = endQuote + 1;
            return true;
        }

        private static bool IsIdentChar(char ch) =>
            (ch is >= 'A' and <= 'Z') ||
            (ch is >= 'a' and <= 'z') ||
            (ch is >= '0' and <= '9') ||
            ch == '_';

    }

    internal static class PowerBIOuterWhereTranslator
    {
        public static bool TryTranslateWhere(Node node, string outerAlias, StringSegment? innerAlias, out QueryExpression expr)
        {
            expr = null;

            if (node == null)
                return false;

            if (node.BoolExpr != null)
                return TryTranslateBoolExpr(node.BoolExpr, outerAlias, innerAlias, out expr);

            if (node.AExpr != null)
                return TryTranslateAExpr(node.AExpr, outerAlias, innerAlias, out expr);

            if (node.NullTest != null)
                return TryTranslateNullTest(node.NullTest, outerAlias, innerAlias, out expr);

            return false;
        }

        private static bool TryTranslateBoolExpr(BoolExpr boolExpr, string outerAlias, StringSegment? innerAlias, out QueryExpression expr)
        {
            expr = null;

            if (boolExpr?.Args == null || boolExpr.Args.Count == 0)
                return false;

            if (boolExpr.Boolop is BoolExprType.AndExpr or BoolExprType.OrExpr)
            {
                var op = boolExpr.Boolop == BoolExprType.AndExpr ? OperatorType.And : OperatorType.Or;

                QueryExpression current = null;
                foreach (var arg in boolExpr.Args)
                {
                    if (TryTranslateWhere(arg, outerAlias, innerAlias, out var translated) == false)
                        return false;

                    current = current == null ? translated : new BinaryExpression(current, translated, op);
                }

                expr = current;
                return expr != null;
            }

            if (boolExpr.Boolop == BoolExprType.NotExpr)
            {
                if (boolExpr.Args.Count != 1)
                    return false;

                if (TryTranslateWhere(boolExpr.Args[0], outerAlias, innerAlias, out var inner) == false)
                    return false;

                if (TryNegate(inner, out expr))
                    return true;

                expr = new NegatedExpression(inner);
                return true;
            }

            return false;
        }

        private static bool TryTranslateAExpr(A_Expr aExpr, string outerAlias, StringSegment? innerAlias, out QueryExpression expr)
        {
            expr = null;

            if (aExpr == null)
                return false;

            if (IsAExprKind(aExpr, A_Expr_Kind.AexprBetween))
            {
                if (aExpr.Lexpr == null || aExpr.Rexpr?.List?.Items == null)
                    return false;

                var items = aExpr.Rexpr.List.Items;
                if (items.Count != 2)
                    return false;

                if (TryExtractFieldExpression(aExpr.Lexpr, outerAlias, innerAlias, out var fieldExpr) == false)
                    return false;

                if (TryExtractConstantValue(items[0], out var lower) == false)
                    return false;

                if (TryExtractConstantValue(items[1], out var upper) == false)
                    return false;

                var ge = new BinaryExpression(fieldExpr, lower, OperatorType.GreaterThanEqual);
                var le = new BinaryExpression(fieldExpr, upper, OperatorType.LessThanEqual);
                expr = new BinaryExpression(ge, le, OperatorType.And);
                return true;
            }

            if (IsAExprKind(aExpr, A_Expr_Kind.AexprIn))
            {
                if (aExpr.Lexpr == null || aExpr.Rexpr == null)
                    return false;

                if (TryExtractFieldExpression(aExpr.Lexpr, outerAlias, innerAlias, out var fieldExpr) == false)
                    return false;

                if (TryExtractConstantList(aExpr.Rexpr, out var values) == false)
                    return false;

                var inExpr = new InExpression(fieldExpr, values, all: false);

                if (TryGetOperatorToken(aExpr, out var opToken) && opToken == "<>")
                {
                    expr = new NegatedExpression(inExpr);
                    return true;
                }

                expr = inExpr;
                return true;
            }

            if (TryGetOperatorToken(aExpr, out var op) == false)
                return false;

            if (TryMapBinaryOperator(op, out var ravenOp) == false)
                return false;

            if (aExpr.Lexpr == null || aExpr.Rexpr == null)
                return false;

            if (TryExtractFieldExpression(aExpr.Lexpr, outerAlias, innerAlias, out var left) == false)
                return false;

            if (TryExtractConstantValue(aExpr.Rexpr, out var right) == false)
                return false;

            expr = new BinaryExpression(left, right, ravenOp);
            return true;
        }

        private static bool IsAExprKind(A_Expr expr, A_Expr_Kind kind)
        {
            if (expr.Kind == kind)
                return true;

            return string.Equals(expr.Kind.ToString(), kind.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetOperatorToken(A_Expr aExpr, out string op)
        {
            op = null;

            if (aExpr?.Name == null || aExpr.Name.Count != 1)
                return false;

            op = aExpr.Name[0].String?.Sval;
            if (string.IsNullOrWhiteSpace(op))
                return false;

            op = op.Trim();
            return true;
        }

        private static bool TryMapBinaryOperator(string op, out OperatorType ravenOp)
        {
            ravenOp = default;

            switch (op)
            {
                case "=":
                    ravenOp = OperatorType.Equal;
                    return true;
                case "!=":
                case "<>":
                    ravenOp = OperatorType.NotEqual;
                    return true;
                case "<":
                    ravenOp = OperatorType.LessThan;
                    return true;
                case "<=":
                    ravenOp = OperatorType.LessThanEqual;
                    return true;
                case ">":
                    ravenOp = OperatorType.GreaterThan;
                    return true;
                case ">=":
                    ravenOp = OperatorType.GreaterThanEqual;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryTranslateNullTest(NullTest nullTest, string outerAlias, StringSegment? innerAlias, out QueryExpression expr)
        {
            expr = null;

            if (nullTest == null)
                return false;

            if (nullTest.Arg == null)
                return false;

            if (TryExtractFieldExpression(nullTest.Arg, outerAlias, innerAlias, out var fieldExpr) == false)
                return false;

            var nullTestType = nullTest.Nulltesttype.ToString();

            if (string.Equals(nullTestType, "IsNull", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nullTestType, "NulltestIsNull", StringComparison.OrdinalIgnoreCase))
            {
                expr = new BinaryExpression(fieldExpr, new ValueExpression("null", ValueTokenType.Null), OperatorType.Equal);
                return true;
            }

            if (string.Equals(nullTestType, "IsNotNull", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nullTestType, "NulltestIsNotNull", StringComparison.OrdinalIgnoreCase))
            {
                expr = new BinaryExpression(fieldExpr, new ValueExpression("null", ValueTokenType.Null), OperatorType.NotEqual);
                return true;
            }

            return false;
        }

        private static bool TryExtractFieldExpression(Node node, string outerAlias, StringSegment? innerAlias, out FieldExpression fieldExpression)
        {
            fieldExpression = null;

            var colRef = node?.ColumnRef;
            if (colRef?.Fields == null)
                return false;

            string column;
            switch (colRef.Fields.Count)
            {
                case 2:
                    var alias = colRef.Fields[0].String?.Sval;
                    if (string.IsNullOrWhiteSpace(alias))
                        return false;

                    if (string.Equals(alias, outerAlias, StringComparison.OrdinalIgnoreCase) == false)
                        return false;

                    column = colRef.Fields[1].String?.Sval;
                    break;

                case 1:
                    column = colRef.Fields[0].String?.Sval;
                    break;

                default:
                    return false;
            }

            if (string.IsNullOrWhiteSpace(column))
                return false;

            var fieldPath = innerAlias != null
                ? new List<StringSegment> { innerAlias.Value, column }
                : new List<StringSegment> { column };

            fieldExpression = new FieldExpression(fieldPath);
            return true;
        }

        private static bool TryExtractConstantValue(Node node, out ValueExpression valueExpression)
        {
            valueExpression = null;

            if (TryExtractTimestampLiteral(node, out valueExpression))
                return true;

            var c = node?.AConst;
            if (c == null)
                return false;

            if (c.Sval != null && string.IsNullOrWhiteSpace(c.Sval.Sval) == false)
            {
                valueExpression = new ValueExpression(c.Sval.Sval, ValueTokenType.String);
                return true;
            }

            if (c.Ival != null)
            {
                valueExpression = new ValueExpression(Convert.ToString(c.Ival.Ival, CultureInfo.InvariantCulture), ValueTokenType.Long);
                return true;
            }

            if (c.Fval != null && string.IsNullOrWhiteSpace(c.Fval.Fval) == false)
            {
                valueExpression = new ValueExpression(c.Fval.Fval, ValueTokenType.Double);
                return true;
            }

            if (c.Boolval != null)
            {
                valueExpression = c.Boolval.Boolval
                    ? new ValueExpression("true", ValueTokenType.True)
                    : new ValueExpression("false", ValueTokenType.False);
                return true;
            }

            return false;
        }

        private static bool TryExtractTimestampLiteral(Node node, out ValueExpression valueExpression)
        {
            valueExpression = null;

            var typeCast = node?.TypeCast;
            if (typeCast == null)
                return false;

            var names = typeCast.TypeName?.Names;
            if (names == null)
                return TryExtractTimestampLiteralViaReflection(node, out valueExpression);

            var hasTimestamp = false;
            foreach (var nameNode in names)
            {
                var s = nameNode?.String?.Sval;
                if (string.Equals(s, "timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    hasTimestamp = true;
                    break;
                }
            }

            if (hasTimestamp == false)
                return false;

            var raw = typeCast.Arg?.AConst?.Sval?.Sval;
            if (raw == null)
                return TryExtractTimestampLiteralViaReflection(node, out valueExpression);

            if (DateTime.TryParse(raw, out var dt) == false)
                return false;

            valueExpression = new ValueExpression(dt.GetDefaultRavenFormat(), ValueTokenType.String);
            return true;
        }

        /// <summary>
        /// Reflection-based fallback for timestamp literal extraction used when the primary strongly-typed
        /// path is not available (e.g. when the <c>TypeCast.TypeName.Names</c> or <c>TypeCast.Arg</c>
        /// properties are absent in the current pgsqlparser build). Accessing via reflection lets us handle
        /// minor API-surface differences across pgsqlparser versions without a hard compile-time dependency
        /// on the exact member layout.
        /// </summary>
        private static bool TryExtractTimestampLiteralViaReflection(Node node, out ValueExpression valueExpression)
        {
            valueExpression = null;

            var typeCastObj = node?.GetType().GetProperty("TypeCast")?.GetValue(node);
            if (typeCastObj == null)
                return false;

            var typeNameObj = typeCastObj.GetType().GetProperty("TypeName")?.GetValue(typeCastObj);
            if (typeNameObj == null)
                return false;

            var typeName = TryGetTypeNameIdentifier(typeNameObj);
            if (string.Equals(typeName, "timestamp", StringComparison.OrdinalIgnoreCase) == false)
                return false;

            var arg = typeCastObj.GetType().GetProperty("Arg")?.GetValue(typeCastObj) as Node;
            var raw = arg?.AConst?.Sval?.Sval;
            if (raw == null)
                return false;

            if (DateTime.TryParse(raw, out var dt) == false)
                return false;

            valueExpression = new ValueExpression(dt.GetDefaultRavenFormat(), ValueTokenType.String);
            return true;
        }

        private static string TryGetTypeNameIdentifier(object typeNameObj)
        {
            var namesObj = typeNameObj.GetType().GetProperty("Names")?.GetValue(typeNameObj) as IEnumerable;
            if (namesObj == null)
                return null;

            foreach (var item in namesObj)
            {
                if (item is Node n)
                {
                    var s = n.String?.Sval;
                    if (string.IsNullOrWhiteSpace(s) == false)
                        return s;
                }
            }

            return null;
        }

        private static bool TryExtractConstantList(Node node, out List<QueryExpression> values)
        {
            values = null;

            var items = node?.List?.Items;
            if (items == null || items.Count == 0)
                return false;

            if (items.Count == 1 && items[0]?.List?.Items != null)
                items = items[0].List.Items;

            values = new List<QueryExpression>(capacity: items.Count);
            foreach (var item in items)
            {
                if (TryExtractConstantValue(item, out var v) == false)
                    return false;
                values.Add(v);
            }

            return values.Count > 0;
        }

        private static bool TryNegate(QueryExpression expr, out QueryExpression negated)
        {
            negated = null;

            if (expr is not BinaryExpression be)
                return false;

            OperatorType inverted;
            switch (be.Operator)
            {
                case OperatorType.Equal:
                    inverted = OperatorType.NotEqual;
                    break;
                case OperatorType.NotEqual:
                    inverted = OperatorType.Equal;
                    break;
                case OperatorType.LessThan:
                    inverted = OperatorType.GreaterThanEqual;
                    break;
                case OperatorType.LessThanEqual:
                    inverted = OperatorType.GreaterThan;
                    break;
                case OperatorType.GreaterThan:
                    inverted = OperatorType.LessThanEqual;
                    break;
                case OperatorType.GreaterThanEqual:
                    inverted = OperatorType.LessThan;
                    break;
                default:
                    return false;
            }

            negated = new BinaryExpression(be.Left, be.Right, inverted);
            return true;
        }
    }
}
