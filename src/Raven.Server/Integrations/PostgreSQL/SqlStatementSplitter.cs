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

            int start = 0;
            int i = 0;
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

                if (c == ';')
                {
                    var piece = sql.Substring(start, i - start).Trim();
                    if (piece.Length > 0)
                        result.Add(piece);
                    i++;
                    start = i;
                    continue;
                }

                i++;
            }

            var last = sql.Substring(start, sql.Length - start).Trim();
            if (last.Length > 0)
                result.Add(last);

            return result;
        }
    }
}
