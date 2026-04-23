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

                if (PgSqlAstHelpers.IsPowerBiWrapperAlias(rss.Alias?.Aliasname) == false)
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
            var idx = SkipWhitespaceForward(s, i);
            if ((uint)idx >= (uint)s.Length || s[idx] != '"')
                return false;

            var endQuote = s.IndexOf('"', idx + 1);
            if (endQuote == -1)
                return false;

            // Reject empty/whitespace aliases like `""` or `"  "`.
            for (int j = idx + 1; j < endQuote; j++)
            {
                if (char.IsWhiteSpace(s[j]) == false)
                    return true;
            }

            return false;
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

            if (SqlWhereParser.TryParse(node, outerAlias, out var parsed) == false)
                return false;

            return TryEmit(parsed, innerAlias, out expr);
        }

        private static bool TryEmit(ParsedWhere parsed, StringSegment? innerAlias, out QueryExpression expr)
        {
            expr = null;

            switch (parsed)
            {
                case ParsedAnd a:
                    return TryEmitBoolean(a.Children, OperatorType.And, innerAlias, out expr);

                case ParsedOr o:
                    return TryEmitBoolean(o.Children, OperatorType.Or, innerAlias, out expr);

                case ParsedNot n:
                    if (TryEmit(n.Child, innerAlias, out var inner) == false)
                        return false;
                    if (TryNegate(inner, out expr))
                        return true;
                    expr = new NegatedExpression(inner);
                    return true;

                case ParsedBinary b:
                    return TryEmitBinary(b, innerAlias, out expr);

                case ParsedIn i:
                    return TryEmitIn(i, innerAlias, out expr);

                case ParsedBetween bt:
                    return TryEmitBetween(bt, innerAlias, out expr);

                case ParsedIsNull nt:
                    return TryEmitIsNull(nt, innerAlias, out expr);

                default:
                    return false;
            }
        }

        private static bool TryEmitBoolean(IReadOnlyList<ParsedWhere> children, OperatorType op, StringSegment? innerAlias, out QueryExpression expr)
        {
            expr = null;
            QueryExpression current = null;
            foreach (var child in children)
            {
                if (TryEmit(child, innerAlias, out var translated) == false)
                    return false;
                current = current == null ? translated : new BinaryExpression(current, translated, op);
            }
            expr = current;
            return expr != null;
        }

        private static bool TryEmitBinary(ParsedBinary b, StringSegment? innerAlias, out QueryExpression expr)
        {
            expr = null;

            if (TryMapBinaryOperator(b.Operator, out var ravenOp) == false)
                return false;

            if (TryBuildFieldExpression(b.FieldPath, innerAlias, out var field) == false)
                return false;

            var value = BuildValueExpression(b.Value);
            if (value == null)
                return false;

            expr = new BinaryExpression(field, value, ravenOp);
            return true;
        }

        private static bool TryEmitIn(ParsedIn i, StringSegment? innerAlias, out QueryExpression expr)
        {
            expr = null;

            if (TryBuildFieldExpression(i.FieldPath, innerAlias, out var field) == false)
                return false;

            var values = new List<QueryExpression>(i.Values.Count);
            foreach (var v in i.Values)
            {
                var ve = BuildValueExpression(v);
                if (ve == null)
                    return false;
                values.Add(ve);
            }

            QueryExpression inExpr = new InExpression(field, values, all: false);
            if (i.Negated)
                inExpr = new NegatedExpression(inExpr);

            expr = inExpr;
            return true;
        }

        private static bool TryEmitBetween(ParsedBetween bt, StringSegment? innerAlias, out QueryExpression expr)
        {
            expr = null;

            if (TryBuildFieldExpression(bt.FieldPath, innerAlias, out var field) == false)
                return false;

            var lower = BuildValueExpression(bt.Lower);
            var upper = BuildValueExpression(bt.Upper);
            if (lower == null || upper == null)
                return false;

            var ge = new BinaryExpression(field, lower, OperatorType.GreaterThanEqual);
            var le = new BinaryExpression(field, upper, OperatorType.LessThanEqual);
            expr = new BinaryExpression(ge, le, OperatorType.And);
            return true;
        }

        private static bool TryEmitIsNull(ParsedIsNull nt, StringSegment? innerAlias, out QueryExpression expr)
        {
            expr = null;

            if (TryBuildFieldExpression(nt.FieldPath, innerAlias, out var field) == false)
                return false;

            var op = nt.Negated ? OperatorType.NotEqual : OperatorType.Equal;
            expr = new BinaryExpression(field, new ValueExpression("null", ValueTokenType.Null), op);
            return true;
        }

        private static bool TryBuildFieldExpression(IReadOnlyList<string> fieldPath, StringSegment? innerAlias, out FieldExpression expr)
        {
            expr = null;
            if (fieldPath == null || fieldPath.Count != 1)
                return false;

            var column = fieldPath[0];
            if (string.IsNullOrWhiteSpace(column))
                return false;

            var segments = innerAlias != null
                ? new List<StringSegment> { innerAlias.Value, column }
                : new List<StringSegment> { column };

            expr = new FieldExpression(segments);
            return true;
        }

        private static ValueExpression BuildValueExpression(ParsedValue value)
        {
            if (value == null)
                return null;

            switch (value.Kind)
            {
                case ParsedValueKind.String:
                    return new ValueExpression((string)value.Raw, ValueTokenType.String);

                case ParsedValueKind.Long:
                    return new ValueExpression(Convert.ToString((long)value.Raw, CultureInfo.InvariantCulture), ValueTokenType.Long);

                case ParsedValueKind.Double:
                    // Raw is either a double (preferred) or the raw string pgsqlparser gave us.
                    if (value.Raw is double d)
                        return new ValueExpression(d.ToString("R", CultureInfo.InvariantCulture), ValueTokenType.Double);
                    return new ValueExpression(value.Raw?.ToString(), ValueTokenType.Double);

                case ParsedValueKind.Bool:
                    return (bool)value.Raw
                        ? new ValueExpression("true", ValueTokenType.True)
                        : new ValueExpression("false", ValueTokenType.False);

                case ParsedValueKind.Timestamp:
                    // Timestamp literals are emitted as Raven-formatted strings (see SqlWhereParser).
                    return new ValueExpression((string)value.Raw, ValueTokenType.String);

                case ParsedValueKind.Null:
                    return new ValueExpression("null", ValueTokenType.Null);

                default:
                    return null;
            }
        }

        private static bool TryMapBinaryOperator(string op, out OperatorType ravenOp)
        {
            ravenOp = default;
            switch (op)
            {
                case "=":  ravenOp = OperatorType.Equal;            return true;
                case "!=":
                case "<>": ravenOp = OperatorType.NotEqual;         return true;
                case "<":  ravenOp = OperatorType.LessThan;         return true;
                case "<=": ravenOp = OperatorType.LessThanEqual;    return true;
                case ">":  ravenOp = OperatorType.GreaterThan;      return true;
                case ">=": ravenOp = OperatorType.GreaterThanEqual; return true;
                default:                                            return false;
            }
        }

        private static bool TryNegate(QueryExpression expr, out QueryExpression negated)
        {
            negated = null;

            if (expr is not BinaryExpression be)
                return false;

            OperatorType inverted;
            switch (be.Operator)
            {
                case OperatorType.Equal:            inverted = OperatorType.NotEqual;         break;
                case OperatorType.NotEqual:         inverted = OperatorType.Equal;            break;
                case OperatorType.LessThan:         inverted = OperatorType.GreaterThanEqual; break;
                case OperatorType.LessThanEqual:    inverted = OperatorType.GreaterThan;      break;
                case OperatorType.GreaterThan:      inverted = OperatorType.LessThanEqual;    break;
                case OperatorType.GreaterThanEqual: inverted = OperatorType.LessThan;         break;
                default:                            return false;
            }

            negated = new BinaryExpression(be.Left, be.Right, inverted);
            return true;
        }
    }
}
