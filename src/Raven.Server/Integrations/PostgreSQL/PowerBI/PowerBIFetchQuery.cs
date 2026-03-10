using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Translation;
using Raven.Server.Documents;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Queries.AST;
using Raven.Server.Documents.Queries.Parser;
using Raven.Server.Integrations.PostgreSQL.Exceptions;
using Sparrow;
using Sparrow.Extensions;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public static class PowerBIFetchQuery
    {
        /// <summary>
        /// Match an SQL from PowerBI that intends to query a collection. The SQL query may be nested and 
        /// may also have a nested RQL query.
        /// </summary>
        private static readonly Regex FetchSqlRegex = new(@"(?is)^\s*(?:select\s+(?:\*|(?:(?:(?:""(\$Table|_)""\.)?""(?<src_columns>[^""]+)""(?:\s+as\s+""(?<all_columns>(?<dest_columns>[^""]+))"")?(?<replace>)|(?<replace>replace)\(""_"".""(?<src_columns>[^""]+)"",\s+'(?<replace_inputs>[^']*)',\s+'(?<replace_texts>[^']*)'\)\s+as\s+""(?<all_columns>(?<dest_columns>[^""]+))"")(?:\s|,)*)+)\s+from\s+(?:(?:\((?:\s|,)*)(?<inner_query>.*)\s*\)|""public"".""(?<table_name>.+)""))\s+""(?:\$Table|_)""(\s+where\s+(?<where>.*?))?(?:\s+limit\s+(?<limit>[0-9]+))?\s*$",
            RegexOptions.Compiled);

        /// <summary>
        /// Match the column names found in the SQL where clause. 
        /// Used to integrate the column names into the where clause of the RQL query.
        /// </summary>
        private static readonly Regex WhereColumnRegex = new(@"""_""\.""(?<column>.*?)""", RegexOptions.Compiled);

        /// <summary>
        /// Match operators found in the SQL where clause. 
        /// Used to integrate the where clause into the RQL query.
        /// </summary>
        private static readonly Regex WhereOperatorRegex = new(@"(?=.*?\s+)is(\s+not)?(?=\s+.+?)", RegexOptions.Compiled);

        /// <summary>
        /// Map of operators from PostgreSQL to RQL
        /// </summary>
        private static readonly Dictionary<string, string> OperatorMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "is", "=" },
            { "is not", "!=" },
        };

        private static readonly Regex TimestampConditionRegex = new(@"timestamp\ \'(?<date>.*?)\'", RegexOptions.Compiled);

        public static bool TryParse(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            if (TryParseSimpleTableFetchViaAst(queryText, parametersDataTypes, documentDatabase, out pgQuery))
                return true;

            if (TryParseWrappedRqlFetchViaAst(queryText, parametersDataTypes, documentDatabase, out pgQuery))
                return true;

            // Match queries sent by PowerBI, either RQL queries wrapped in an SQL statement OR generic SQL queries
            if (TryGetMatches(queryText, out var matches, out var rql) == false)
            {
                pgQuery = null;
                return false;
            }

            Dictionary<string, ReplaceColumnValue> powerBiReplaceValues = GetReplaceValues(matches);

            string newRql = null;

            if (rql != null)
            {
                // RQL query coming  from 'SQL statement (optional, requires database)' text box in Power BI

                var powerBiFiltering = GetSqlWhereConditions(matches, rql.From.Alias);

                if (powerBiFiltering != null)
                {
                    if (rql.Where == null)
                        rql.Where = powerBiFiltering;
                    else
                        rql.Where = new BinaryExpression(rql.Where, powerBiFiltering, OperatorType.And);

                    newRql = rql.ToString();
                }
                else
                {
                    newRql = rql.QueryText;
                }
            }
            else if (matches[0].Groups["table_name"].Success)
            {
                // SQL query coming from selecting an loading entire collection (table)

                if (matches.Count != 1)
                    throw new PgErrorException(PgErrorCodes.StatementTooComplex,
                        "Unexpected PowerBI nested SQL query. Query: " + queryText);

                var sqlQuery = matches[0];

                string tableName = sqlQuery.Groups["table_name"].Value;

                newRql = $"from '{tableName}'";
            }

            if (newRql == null)
            {
                pgQuery = null;
                return false;
            }

            var limit = matches[0].Groups["limit"];

            pgQuery = new PowerBIRqlQuery(newRql, parametersDataTypes, documentDatabase, powerBiReplaceValues, limit.Success ? int.Parse(limit.Value) : null);

            return true;
        }

        private static bool TryParseWrappedRqlFetchViaAst(string queryText, int[] parametersDataTypes, DocumentDatabase documentDatabase, out PgQuery pgQuery)
        {
            pgQuery = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            try
            {
                var sql = queryText;

                if (TryExtractDeepestInnerRqlSpan(sql, out var innerStart, out var innerEnd, out var innerRql) == false)
                    return false;

                if (innerRql.StartsWith("from", StringComparison.OrdinalIgnoreCase) == false)
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

                if (TryExtractNonNegativeIntAConst(selectStmt.LimitCount, out var limit) == false)
                    return false;

                Raven.Server.Documents.Queries.AST.Query q;
                try
                {
                    q = QueryMetadata.ParseQuery(innerRql, QueryType.Select);
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
                        if (OuterWhereTranslator.TryTranslateWhere(currentSelect.WhereClause, wrapperAlias, q.From.Alias, out var whereExpression) == false)
                            return false;

                        q.Where = q.Where == null
                            ? whereExpression
                            : new BinaryExpression(q.Where, whereExpression, OperatorType.And);
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

                var newRql = q.ToString();

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

        private static bool TryExtractNonNegativeIntAConst(Node node, out int value)
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

            if (AstSqlToRqlTranslator.TryParse(queryText, parametersDataTypes, out var rql) == false)
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

        private static Dictionary<string, ReplaceColumnValue> GetReplaceValues(List<Match> matches)
        {
            Dictionary<string, ReplaceColumnValue> replaceValues = null;

            foreach (var matchToCheck in matches)
            {
                var replaceGroup = matchToCheck.Groups["replace"];

                if (replaceGroup.Success)
                {
                    if (string.IsNullOrEmpty(replaceGroup.Value) == false)
                    {
                        // Populate the replace columns starting from the inner-most SQL

                        replaceValues = new Dictionary<string, ReplaceColumnValue>();

                        for (var i = matches.Count - 1; i >= 0; i--)
                        {
                            replaceValues = GetReplaces(matches[i], ref replaceValues);
                        }

                        break;
                    }
                }
            }

            return replaceValues;
        }

        private static QueryExpression GetSqlWhereConditions(List<Match> matches, StringSegment? alias)
        {
            List<QueryExpression> whereExpressions = null;

            foreach (var matchToCheck in matches)
            {
                var whereGroup = matchToCheck.Groups["where"];

                if (whereGroup.Success)
                {
                    if (string.IsNullOrEmpty(whereGroup.Value) == false)
                    {
                        var whereFilteringCondition = whereGroup.Value;

                        var replaceValue = "${column}";

                        if (alias != null)
                            replaceValue = $"{alias}." + replaceValue;

                        whereFilteringCondition = WhereColumnRegex.Replace(whereFilteringCondition, replaceValue);

                        whereFilteringCondition = WhereOperatorRegex.Replace(whereFilteringCondition, (m) =>
                        {
                            if (OperatorMap.TryGetValue(m.Value, out var val))
                                return val;

                            return m.Value;
                        });

                        whereFilteringCondition = TimestampConditionRegex.Replace(whereFilteringCondition, timestampMatch =>
                        {
                            if (timestampMatch.Success)
                            {
                                var dateGroup = timestampMatch.Groups["date"];

                                if (dateGroup.Success && DateTime.TryParse(dateGroup.Value, out var date))
                                {
                                    return $"'{date.GetDefaultRavenFormat()}'";
                                }
                            }

                            return timestampMatch.Value;
                        });

                        var parser = new QueryParser();

                        parser.Init(whereFilteringCondition);

                        if (parser.Expression(out var parsedConditions) == false)
                        {
                            throw new NotSupportedException("Unable to parse WHERE clause: " + whereFilteringCondition);
                        }

                        whereExpressions ??= new List<QueryExpression>();

                        whereExpressions.Add(parsedConditions);
                    }
                }
            }

            if (whereExpressions == null)
                return null;

            if (whereExpressions.Count == 1)
                return whereExpressions[0];

            BinaryExpression result = null;

            for (int i = 1; i < whereExpressions.Count; i++)
            {
                if (result == null)
                    result = new BinaryExpression(whereExpressions[0], whereExpressions[1], OperatorType.And);
                else
                    result = new BinaryExpression(whereExpressions[i], result, OperatorType.And);
            }

            return result;
        }

        private static bool TryGetMatches(string queryText, out List<Match> outMatches, out Raven.Server.Documents.Queries.AST.Query rql)
        {
            var matches = new List<Match>();
            var queryToMatch = queryText;
            Group innerQuery;

            rql = null;

            // Queries can have inner queries that we need to parse, so here we collect those
            do
            {
                var match = FetchSqlRegex.Match(queryToMatch);

                if (!match.Success)
                {
                    outMatches = null;
                    return false;
                }

                matches.Add(match);

                innerQuery = match.Groups["inner_query"];
                queryToMatch = match.Groups["inner_query"].Value;
            } while (innerQuery.Success && IsRql(queryToMatch, out rql) == false);

            outMatches = matches;
            return true;
        }

        private static Dictionary<string, ReplaceColumnValue> GetReplaces(Match match, ref Dictionary<string, ReplaceColumnValue> replaces)
        {
            var destColumns = match.Groups["dest_columns"].Captures;
            var srcColumns = match.Groups["src_columns"].Captures;
            var replace = match.Groups["replace"].Captures;
            var replaceInputs = match.Groups["replace_inputs"].Captures;
            var replaceTexts = match.Groups["replace_texts"].Captures;

            var replaceIndex = 0;
            for (var i = 0; i < destColumns.Count; i++)
            {
                var destColumn = destColumns[i].Value;
                var srcColumn = srcColumns[i].Value;

                if (replace[i].Value.Length != 0)
                {
                    replaces[srcColumn] = new ReplaceColumnValue
                    {
                        DstColumnName = destColumn,
                        SrcColumnName = srcColumn,
                        OldValue = replaceInputs[replaceIndex].Value,
                        NewValue = replaceTexts[replaceIndex].Value,
                    };

                    replaceIndex++;
                }
            }

            return replaces;
        }

        private static bool IsRql(string queryText, out Documents.Queries.AST.Query query)
        {
            try
            {
                query = QueryMetadata.ParseQuery(queryText, QueryType.Select);
            }
            catch
            {
                query = null;
                return false;
            }

            return true;
        }
    }
}
