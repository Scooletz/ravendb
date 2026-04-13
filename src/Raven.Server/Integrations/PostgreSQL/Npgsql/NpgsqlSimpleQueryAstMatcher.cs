using System;
using PgSqlParser;
using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL.Npgsql
{
    /// <summary>
    /// AST-based matcher for simple Npgsql init queries: <c>version()</c>,
    /// <c>current_setting('max_index_keys')</c>, and their combined two-statement form.
    /// Tolerates whitespace, case, and comment variations that would trip up string comparison.
    /// </summary>
    internal static class NpgsqlSimpleQueryAstMatcher
    {
        public static bool TryMatch(string queryText, out PgTable result)
        {
            result = null;

            if (string.IsNullOrWhiteSpace(queryText))
                return false;

            var parseResult = Parser.Parse(queryText);
            if (parseResult.IsSuccess == false || parseResult.Value?.Stmts == null)
                return false;

            var stmts = parseResult.Value.Stmts;

            switch (stmts.Count)
            {
                case 1:
                {
                    var select = stmts[0]?.Stmt?.SelectStmt;
                    if (select == null)
                        return false;

                    if (IsVersionCall(select))
                    {
                        result = NpgsqlConfig.VersionResponse;
                        return true;
                    }

                    if (IsCurrentSettingMaxIndexKeysCall(select))
                    {
                        result = NpgsqlConfig.CurrentSettingResponse;
                        return true;
                    }

                    return false;
                }

                case 2:
                {
                    // Npgsql sends both queries as a single multi-statement message on some code paths:
                    //   SELECT version(); SELECT current_setting('max_index_keys')
                    var first = stmts[0]?.Stmt?.SelectStmt;
                    var second = stmts[1]?.Stmt?.SelectStmt;

                    if (first != null && second != null &&
                        IsVersionCall(first) && IsCurrentSettingMaxIndexKeysCall(second))
                    {
                        result = NpgsqlConfig.VersionCurrentSettingResponse;
                        return true;
                    }

                    return false;
                }

                default:
                    return false;
            }
        }

        // SELECT version()  — no FROM, single unqualified zero-arg function call.
        private static bool IsVersionCall(SelectStmt s)
        {
            if (s.FromClause is { Count: > 0 })
                return false;

            if (s.TargetList is not { Count: 1 })
                return false;

            var funcCall = s.TargetList[0]?.ResTarget?.Val?.FuncCall;
            return IsSingleUnqualifiedCall(funcCall, "version", argCount: 0);
        }

        // SELECT current_setting('max_index_keys')  — only the known Npgsql init key is claimed.
        private static bool IsCurrentSettingMaxIndexKeysCall(SelectStmt s)
        {
            if (s.FromClause is { Count: > 0 })
                return false;

            if (s.TargetList is not { Count: 1 })
                return false;

            var funcCall = s.TargetList[0]?.ResTarget?.Val?.FuncCall;
            if (IsSingleUnqualifiedCall(funcCall, "current_setting", argCount: 1) == false)
                return false;

            var argValue = funcCall.Args[0].AConst?.Sval?.Sval;
            return string.Equals(argValue, "max_index_keys", StringComparison.OrdinalIgnoreCase);
        }

        // Unqualified = exactly one component in Funcname (no schema prefix).
        private static bool IsSingleUnqualifiedCall(FuncCall funcCall, string expectedName, int argCount)
        {
            if (funcCall == null)
                return false;

            // Unqualified name = exactly one component in Funcname.
            if (funcCall.Funcname is not { Count: 1 })
                return false;

            var name = funcCall.Funcname[0].String?.Sval;
            if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            return (funcCall.Args?.Count ?? 0) == argCount;
        }
    }
}
