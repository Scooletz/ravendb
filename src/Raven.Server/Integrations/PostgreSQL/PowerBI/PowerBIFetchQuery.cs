using System;
using System.Collections.Generic;
using System.Globalization;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Translation;
using Raven.Server.Documents;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Queries.AST;
using Raven.Server.Documents.Queries.Parser;
using Sparrow;
using Sparrow.Extensions;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public static class PowerBIFetchQuery
    {
        public static bool TryParse(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            if (TryParseSimpleTableFetchViaAst(queryText, parametersDataTypes, documentDatabase, out pgQuery))
                return true;

            if (TryParseWrappedRqlFetchViaAst(queryText, parametersDataTypes, documentDatabase, out pgQuery))
                return true;

            pgQuery = null;
            return false;
        }

        private static bool TryParseWrappedRqlFetchViaAst(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            pgQuery = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            try
            {
                var sql = queryText;

                if (PowerBIInnerRqlExtractor.TryExtractInnerRqlSpan(sql, out var innerStart, out var innerEnd, out var innerRql) == false)
                    return false;

                var sanitizedSql = sql[..innerStart] + "select 1" + sql[innerEnd..];

                var parseResult = Parser.Parse(sanitizedSql);
                if (parseResult.IsSuccess == false || parseResult.Value == null)
                    return false;

                if (parseResult.Value.Stmts == null || parseResult.Value.Stmts.Count != 1)
                    return false;

                var stmt = parseResult.Value.Stmts[0];
                var selectStmt = stmt?.Stmt?.SelectStmt;
                if (selectStmt == null)
                    return false;

                if (selectStmt.LimitOffset != null)
                    return false;

                if (TryExtractLimit(selectStmt.LimitCount, out var limit) == false)
                    return false;

                Documents.Queries.AST.Query query;
                try
                {
                    query = QueryMetadata.ParseQuery(innerRql, QueryType.Select);
                }
                catch
                {
                    return false;
                }

                Dictionary<string, ReplaceColumnValue> allReplaces = null;
                List<Dictionary<string, ReplaceColumnValue>> wrapperReplaces = null;

                var currentSelect = selectStmt;
                var isOuterMost = true;

                while (true)
                {
                    if (IsWrapperRangeSubselectSelect(currentSelect, out var wrapperAlias, out var nextSelect) == false)
                    {
                        if (isOuterMost)
                            return false;

                        break;
                    }

                    if (isOuterMost == false)
                    {
                        if (currentSelect.LimitCount != null || currentSelect.LimitOffset != null)
                            return false;
                    }

                    if (TryExtractPowerBiReplaceColumns(currentSelect, wrapperAlias, out var levelReplaces) == false)
                        return false;

                    if (levelReplaces != null)
                    {
                        wrapperReplaces ??= new List<Dictionary<string, ReplaceColumnValue>>();
                        wrapperReplaces.Add(levelReplaces);
                    }

                    if (currentSelect.WhereClause != null)
                    {
                        if (PowerBIOuterWhereTranslator.TryTranslateWhere(currentSelect.WhereClause, wrapperAlias, query.From.Alias, out var whereExpression) == false)
                            return false;

                        query.Where = query.Where == null
                            ? whereExpression
                            : new BinaryExpression(query.Where, whereExpression, OperatorType.And);
                    }

                    if (nextSelect == null)
                        break;

                    currentSelect = nextSelect;
                    isOuterMost = false;
                }

                if (wrapperReplaces != null)
                {
                    for (int i = wrapperReplaces.Count - 1; i >= 0; i--)
                    {
                        allReplaces ??= new Dictionary<string, ReplaceColumnValue>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kvp in wrapperReplaces[i])
                            allReplaces[kvp.Key] = kvp.Value;
                    }
                }

                var newRql = query.ToString();

                pgQuery = new PowerBIRqlQuery(newRql, parametersDataTypes, documentDatabase, allReplaces, limit: limit);
                return true;
            }
            catch
            {
                pgQuery = null;
                return false;
            }

            static bool IsWrapperRangeSubselectSelect(SelectStmt s, out string alias, out SelectStmt nextSelect)
            {
                alias = null;
                nextSelect = null;

                if (s.FromClause is not { Count: 1 })
                    return false;

                var fromItem = s.FromClause[0];
                if (fromItem?.RangeSubselect == null)
                    return false;

                if (fromItem.RangeVar != null || fromItem.JoinExpr != null)
                    return false;

                var rss = fromItem.RangeSubselect;
                alias = rss.Alias?.Aliasname;
                if (string.IsNullOrWhiteSpace(alias))
                    return false;

                if (string.Equals(alias, "_", StringComparison.OrdinalIgnoreCase) == false &&
                    string.Equals(alias, "$Table", StringComparison.OrdinalIgnoreCase) == false)
                    return false;

                nextSelect = rss.Subquery?.SelectStmt;
                return true;
            }
        }

        private static bool TryExtractInnerRqlSpanViaTwoParsers(string sql, out int innerStart, out int innerEnd, out string innerRql)
        {
            innerStart = 0;
            innerEnd = 0;
            innerRql = null;

            if (string.IsNullOrWhiteSpace(sql))
                return false;

            // This is intentionally conservative: we rely on pgsqlparser's failure cursor pointing at the start
            // of the embedded RQL. If this doesn't hold for some query shape, the legacy fallback extractor
            // (`TryExtractDeepestInnerRqlSpan`) is expected to handle it.
            var parseResult = Parser.Parse(sql);

            if (parseResult.IsSuccess)
                return false;

            var cursorPos1Based = parseResult.Error?.CursorPos ?? 0;
            if (cursorPos1Based <= 0)
                return false;

            var start = cursorPos1Based - 1;
            if (TryNormalizeStartIndex(sql, start, out start) == false)
                return false;

            if (StartsWithKeywordAtWordBoundary(sql, start, "from") == false &&
                StartsWithKeywordAtWordBoundary(sql, start, "declare") == false)
                return false;

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

        // Helpers for cursor-based inner RQL extraction (intentionally conservative)

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

        private static bool TryReadQuotedAlias(string s, int i, out string alias, out int nextIndex)
        {
            alias = null;
            nextIndex = 0;

            var idx = SkipWhitespaceForward(s, i);
            if ((uint)idx >= (uint)s.Length || s[idx] != '"')
                return false;

            // In PowerBI wrappers, the inner RQL is placed inside a SQL subselect `( <RQL> ) "<alias>"`.
            // Accept any quoted alias here; wrappers commonly use `_`, `$Table`, `rows`, etc.
            // This keeps the extractor conservative about the RQL span itself while preventing false negatives.
            var endQuote = s.IndexOf('"', idx + 1);
            if (endQuote == -1)
                return false;

            alias = s.Substring(idx + 1, endQuote - idx - 1);
            nextIndex = endQuote + 1;
            return string.IsNullOrWhiteSpace(alias) == false;
        }

        private static bool TryConsumeQuotedAlias(string s, int i)
        {
            return TryReadQuotedAlias(s, i, out _, out _);
        }

        private static bool IsIdentChar(char ch) =>
            (ch is >= 'A' and <= 'Z') ||
            (ch is >= 'a' and <= 'z') ||
            (ch is >= '0' and <= '9') ||
            ch == '_';

        private static bool TryExtractDeepestInnerRqlSpan(string sql, out int innerStartAbs, out int innerEndAbs, out string innerRql)
        {
            innerStartAbs = 0;
            innerEndAbs = 0;
            innerRql = null;

            var currentSlice = sql;
            var baseOffsetAbs = 0;
            var found = false;

            while (TryExtractInnermostRql(currentSlice, out var extractedInnerRql, out var currentStartRel, out var currentEndRel))
            {
                innerStartAbs = baseOffsetAbs + currentStartRel;
                innerEndAbs = baseOffsetAbs + currentEndRel;
                innerRql = extractedInnerRql;
                found = true;

                baseOffsetAbs += currentStartRel;
                currentSlice = extractedInnerRql;
            }

            return found;
        }

        private static bool TryExtractPowerBiReplaceColumns(SelectStmt selectStmt, string outerAlias, out Dictionary<string, ReplaceColumnValue> replaces)
        {
            replaces = null;

            var targets = selectStmt?.TargetList;
            if (targets == null || targets.Count == 0)
                return true;

            foreach (var t in targets)
            {
                var resTarget = t?.ResTarget;
                var funcCall = resTarget?.Val?.FuncCall;
                if (funcCall == null)
                    continue;

                var funcName = funcCall.Funcname is { Count: > 0 }
                    ? funcCall.Funcname[0].String?.Sval
                    : null;

                if (string.Equals(funcName, "replace", StringComparison.OrdinalIgnoreCase) == false)
                    return false;

                var dstColumn = resTarget.Name;
                if (string.IsNullOrWhiteSpace(dstColumn))
                    return false;

                if (funcCall.Args is not { Count: 3 })
                    return false;

                var arg0 = funcCall.Args[0];
                if (arg0?.ColumnRef?.Fields is not { Count: 2 } fields)
                    return false;

                var arg0Alias = fields[0].String?.Sval;
                if (string.IsNullOrWhiteSpace(arg0Alias) || string.Equals(arg0Alias, outerAlias, StringComparison.OrdinalIgnoreCase) == false)
                    return false;

                var srcColumn = fields[1].String?.Sval;
                if (string.IsNullOrWhiteSpace(srcColumn))
                    return false;

                var oldValue = funcCall.Args[1]?.AConst?.Sval?.Sval;
                var newValue = funcCall.Args[2]?.AConst?.Sval?.Sval;

                if (oldValue == null || newValue == null)
                    return false;

                replaces ??= new Dictionary<string, ReplaceColumnValue>(StringComparer.OrdinalIgnoreCase);
                replaces[srcColumn] = new ReplaceColumnValue
                {
                    SrcColumnName = srcColumn,
                    DstColumnName = dstColumn,
                    OldValue = oldValue,
                    NewValue = newValue
                };
            }

            return true;
        }

        private static class OuterWhereTranslator
        {
            // TODO RavenDB-26030: This overlaps significantly with `AstSqlToRqlTranslator.TranslateWhereClause()`.
            // We should eventually share/extract a single PgSqlParser->Raven QueryExpression translator.

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

                    // Best-effort generic negation for supported AST nodes.
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
                var namesObj = typeNameObj.GetType().GetProperty("Names")?.GetValue(typeNameObj) as System.Collections.IEnumerable;
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

        private static bool TryExtractInnermostRql(string sql, out string innerRql, out int innerStart, out int innerEnd)
        {
            innerRql = null;
            innerStart = 0;
            innerEnd = 0;

            const string endTokenTable = ") \"$Table\"";
            const string endTokenUnderscore = ") \"_\"";

            var endTable = sql.LastIndexOf(endTokenTable, StringComparison.OrdinalIgnoreCase);
            var endUnderscore = sql.LastIndexOf(endTokenUnderscore, StringComparison.OrdinalIgnoreCase);

            var end = Math.Max(endTable, endUnderscore);
            if (end == -1)
                return false;

            int depth = 0;
            int open = -1;
            for (int i = end - 1; i >= 0; i--)
            {
                var ch = sql[i];
                if (ch == ')')
                {
                    depth++;
                    continue;
                }

                if (ch != '(')
                    continue;

                if (depth > 0)
                {
                    depth--;
                    continue;
                }

                open = i;
                break;
            }

            if (open == -1)
                return false;

            int j = open - 1;
            while (j >= 0 && char.IsWhiteSpace(sql[j]))
                j--;

            if (j < 3)
                return false;

            var fromStart = j - 3;
            if (fromStart < 0)
                return false;

            if (string.Equals(sql.Substring(fromStart, 4), "from", StringComparison.OrdinalIgnoreCase) == false)
                return false;

            var untrimmedStart = open + 1;
            var untrimmedEnd = end;
            if (untrimmedStart >= untrimmedEnd)
                return false;

            var trimmedStart = untrimmedStart;
            var trimmedEnd = untrimmedEnd;
            while (trimmedStart < trimmedEnd && char.IsWhiteSpace(sql[trimmedStart]))
                trimmedStart++;
            while (trimmedEnd > trimmedStart && char.IsWhiteSpace(sql[trimmedEnd - 1]))
                trimmedEnd--;

            if (trimmedStart >= trimmedEnd)
                return false;

            innerRql = sql.Substring(trimmedStart, trimmedEnd - trimmedStart);

            innerStart = trimmedStart;
            innerEnd = trimmedEnd;
            return string.IsNullOrWhiteSpace(innerRql) == false;
        }

        private static bool TryExtractLimit(Node node, out int value)
        {
            value = 0;

            var c = node?.AConst;
            if (c == null)
                return false;

            if (c.Sval != null && int.TryParse(c.Sval.Sval, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return value >= 0;

            if (c.Ival != null)
            {
                value = (int)c.Ival.Ival;
                return value >= 0;
            }

            if (c.Fval != null && int.TryParse(c.Fval.Fval, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return value >= 0;

            return false;
        }

        private static bool TryParseSimpleTableFetchViaAst(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            pgQuery = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            // RavenDB-26030: temporary bridge.
            // Only attempt this for the simple PowerBI Import table fetch shape:
            //   SELECT ... FROM "public"."<Collection>" <alias?> [WHERE ...] [LIMIT/OFFSET ...]
            // Everything else should fall back to the existing regex-based PowerBI parser.
            if (IsSimplePublicRangeVarSelect(queryText, out _) == false)
                return false;

            if (PgSqlToRqlTranslator.TryParse(queryText, parametersDataTypes, out var rql) == false)
                return false;

            pgQuery = new PowerBIRqlQuery(rql, parametersDataTypes, documentDatabase, replaces: null, limit: null);
            return true;
        }

        private static bool IsSimplePublicRangeVarSelect(string sql, out string aliasName)
        {
            aliasName = null;

            var parseResult = Parser.Parse(sql);
            if (parseResult.IsSuccess == false || parseResult.Value == null)
                return false;

            if (parseResult.Value.Stmts == null || parseResult.Value.Stmts.Count != 1)
                return false;

            var stmt = parseResult.Value.Stmts[0];
            var select = stmt?.Stmt?.SelectStmt;
            if (select == null)
                return false;

            if (select.FromClause is not { Count: 1 })
                return false;

            var rangeVar = select.FromClause[0]?.RangeVar;
            if (rangeVar == null)
                return false;

            if (string.Equals(rangeVar.Schemaname, "public", StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (string.IsNullOrWhiteSpace(rangeVar.Relname))
                return false;

            aliasName = rangeVar.Alias?.Aliasname;
            return true;
        }

        
    }
}
