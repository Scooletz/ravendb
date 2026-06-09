using System;
using System.Collections.Generic;

namespace Raven.Server.Integrations.PostgreSQL
{
    // Splits a SQL string into individual statements on `;`, ignoring semicolons that appear
    // inside string literals (single quoted, with PG '' / \' escapes), quoted identifiers
    // ("..."), line comments (-- to newline), block comments (/* ... */), or dollar-quoted
    // strings ($tag$...$tag$). Used by the Simple Query Protocol handler so that multi-statement
    // batches like pgAdmin's startup probe — `SET DateStyle=ISO; SET client_min_messages=notice;
    // SELECT … ; SET client_encoding='utf-8'` — get dispatched one statement at a time.
    internal static class SqlStatementSplitter
    {
        public static List<string> Split(string sql)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(sql))
                return result;

            // RQL queries have no `;`-separator semantics — they're always a single statement
            // on the wire — but they routinely contain `;` inside `declare function { ... }`
            // JavaScript bodies. A SQL-shaped splitter would shred those into invalid pieces.
            // Bypass the splitter entirely for inputs whose leading keyword is RQL.
            //
            // We do NOT strip a trailing `;` — that's invalid RQL and surfacing the parser
            // error is the right behavior (the user should drop the `;`). The pass-through is
            // about preserving the semantically-meaningful contents, not normalizing them.
            if (LooksLikeRql(sql))
            {
                result.Add(sql.Trim());
                return result;
            }

            int start = 0;
            int i = 0;
            // PG `;` is a top-level statement terminator; never a statement boundary inside
            // a `(...)` subquery / function-arg list. PowerBI's schema-discovery probe wraps
            // user SQL/RQL as `select * from (USER_QUERY) "_" limit 0` — if USER_QUERY is RQL
            // with a `declare function { var x = ...; return ...; }` body, the JS semicolons
            // are nested inside the outer `(...)`. Without depth tracking we'd shred the JS
            // body into "statements" that look like garbage to the dispatcher.
            // Brace depth is tracked separately so a `;` inside `{...}` (RQL declare-function
            // JS body, even if not wrapped in outer parens) is also preserved.
            int parenDepth = 0;
            int braceDepth = 0;
            while (i < sql.Length)
            {
                char c = sql[i];

                // Line comment: -- to end of line.
                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    i += 2;
                    while (i < sql.Length && sql[i] != '\n')
                        i++;
                    continue;
                }

                // Block comment: /* ... */ (no nesting — PG actually does allow nesting but
                // virtually no client emits nested block comments; flat handling is fine here).
                if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                        i++;
                    i = Math.Min(i + 2, sql.Length);
                    continue;
                }

                // Single-quoted string literal: '...'. Handles PG '' escape and the backslash
                // escape that E'...' literals use.
                if (c == '\'')
                {
                    i++;
                    while (i < sql.Length)
                    {
                        if (sql[i] == '\\' && i + 1 < sql.Length)
                        {
                            i += 2;
                            continue;
                        }
                        if (sql[i] == '\'')
                        {
                            if (i + 1 < sql.Length && sql[i + 1] == '\'')
                            {
                                i += 2;
                                continue;
                            }
                            i++;
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                // Double-quoted identifier: "..." (no escape semantics for our purposes — PG
                // uses "" to escape a single double-quote, but we only care about not splitting
                // on `;` inside).
                if (c == '"')
                {
                    i++;
                    while (i < sql.Length)
                    {
                        if (sql[i] == '"')
                        {
                            if (i + 1 < sql.Length && sql[i + 1] == '"')
                            {
                                i += 2;
                                continue;
                            }
                            i++;
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                // Dollar-quoted string: $tag$ ... $tag$ (tag may be empty: $$...$$).
                if (c == '$')
                {
                    int tagEnd = i + 1;
                    while (tagEnd < sql.Length && sql[tagEnd] != '$')
                    {
                        var ch = sql[tagEnd];
                        if (char.IsLetterOrDigit(ch) || ch == '_')
                        {
                            tagEnd++;
                            continue;
                        }
                        break;
                    }
                    if (tagEnd < sql.Length && sql[tagEnd] == '$')
                    {
                        var tag = sql.Substring(i, tagEnd - i + 1);
                        int close = sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);
                        if (close > 0)
                        {
                            i = close + tag.Length;
                            continue;
                        }
                    }
                    i++;
                    continue;
                }

                if (c == '(')
                {
                    parenDepth++;
                    i++;
                    continue;
                }
                if (c == ')')
                {
                    if (parenDepth > 0)
                        parenDepth--;
                    i++;
                    continue;
                }
                if (c == '{')
                {
                    braceDepth++;
                    i++;
                    continue;
                }
                if (c == '}')
                {
                    if (braceDepth > 0)
                        braceDepth--;
                    i++;
                    continue;
                }

                if (c == ';')
                {
                    if (parenDepth == 0 && braceDepth == 0)
                    {
                        var piece = sql.Substring(start, i - start).Trim();
                        if (piece.Length > 0)
                            result.Add(piece);
                        i++;
                        start = i;
                        continue;
                    }
                    // Nested `;` — part of a JS body or other inner content, not a statement boundary.
                    i++;
                    continue;
                }

                i++;
            }

            var last = sql.Substring(start, sql.Length - start).Trim();
            if (last.Length > 0)
                result.Add(last);

            return result;
        }

        // Skips leading whitespace and SQL comments, then checks whether the first non-trivial
        // token is an RQL-only keyword (`declare` for a JS function declaration, `from` for the
        // standard collection query). PG SQL never starts a statement with these — `FROM` only
        // appears mid-SELECT — so the keyword anchor is a reliable signal that the entire input
        // is RQL and must not be `;`-split.
        private static bool LooksLikeRql(string sql)
        {
            int i = 0;
            while (i < sql.Length)
            {
                var ch = sql[i];
                if (char.IsWhiteSpace(ch))
                {
                    i++;
                    continue;
                }
                if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    i += 2;
                    while (i < sql.Length && sql[i] != '\n')
                        i++;
                    continue;
                }
                if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                        i++;
                    i = Math.Min(i + 2, sql.Length);
                    continue;
                }
                break;
            }

            return StartsWithKeywordAtWordBoundary(sql, i, "declare")
                || StartsWithKeywordAtWordBoundary(sql, i, "from");
        }

        private static bool StartsWithKeywordAtWordBoundary(string sql, int i, string keyword)
        {
            if (i + keyword.Length > sql.Length)
                return false;
            for (int k = 0; k < keyword.Length; k++)
            {
                if (char.ToLowerInvariant(sql[i + k]) != keyword[k])
                    return false;
            }
            int next = i + keyword.Length;
            if (next == sql.Length)
                return true;
            var nc = sql[next];
            return char.IsLetterOrDigit(nc) == false && nc != '_';
        }
    }
}
