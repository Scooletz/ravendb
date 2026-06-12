using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Raven.Server.Integrations.PostgreSQL.Exceptions;
using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL
{
    /// <summary>
    /// Handles the small set of non-SELECT protocol-level statements that clients send for
    /// connection / session housekeeping rather than data:
    /// <list type="bullet">
    ///   <item><c>DISCARD ALL</c> - resets the session; we have nothing to discard, so the response is empty.</item>
    ///   <item><c>ROLLBACK</c> - no-op for our read-only protocol surface.</item>
    ///   <item><c>BEGIN</c> / <c>COMMIT</c> - no-op (no transactions on this PG surface).</item>
    ///   <item><c>SET ...</c> - silently accepted; we don't actually track session variables.</item>
    ///   <item><c>SELECT set_config(...)</c> - pgAdmin sends this in its startup batch; treat as no-op.</item>
    ///   <item><c>DEALLOCATE "&lt;name&gt;"</c> - frees a named prepared statement; removes it from the session.</item>
    /// </list>
    /// These cannot be expressed as SQL/AST shapes so they don't fit the virtual-tables interpreter.
    /// They live in a dedicated dispatch slot at the head of <see cref="PgQuery.CreateInstance"/>.
    /// </summary>
    public sealed class ProtocolCommandQuery : PgQuery
    {
        private const string DiscardAll = "DISCARD ALL";
        private const string Rollback   = "ROLLBACK";
        private const string Begin      = "BEGIN";
        private const string Commit     = "COMMIT";
        private const string Set        = "SET";
        private const string Deallocate = "DEALLOCATE";

        private ProtocolCommandQuery(string queryString, int[] parametersDataTypes)
            : base(queryString, parametersDataTypes)
        {
        }

        public static bool TryParse(string queryText, int[] parametersDataTypes, PgSession session, out ProtocolCommandQuery query)
        {
            query = null;

            var normalized = queryText.NormalizeLineEndings();

            if (normalized.StartsWith(DiscardAll, StringComparison.OrdinalIgnoreCase))
            {
                query = new ProtocolCommandQuery(queryText, parametersDataTypes);
                return true;
            }

            if (normalized.StartsWith(Rollback, StringComparison.OrdinalIgnoreCase))
            {
                query = new ProtocolCommandQuery(queryText, parametersDataTypes);
                return true;
            }

            if (StartsWithWord(normalized, Begin) || StartsWithWord(normalized, Commit))
            {
                query = new ProtocolCommandQuery(queryText, parametersDataTypes);
                return true;
            }

            if (StartsWithWord(normalized, Set))
            {
                // SET DateStyle=ISO / SET client_encoding='utf-8' / SET client_min_messages=notice
                // - we don't honour any of these but accept them so clients can finish their
                // session handshake. Returns an empty CommandComplete.
                query = new ProtocolCommandQuery(queryText, parametersDataTypes);
                return true;
            }

            if (IsSelectSetConfig(normalized))
            {
                // pgAdmin emits `SELECT set_config('bytea_output','hex',false) FROM
                // pg_show_all_settings() WHERE name = 'bytea_output'` as part of its startup batch.
                // We can't honour set_config and don't model pg_show_all_settings(), but accepting
                // the statement as a no-op unblocks the connection.
                query = new ProtocolCommandQuery(queryText, parametersDataTypes);
                return true;
            }

            if (normalized.StartsWith(Deallocate, StringComparison.OrdinalIgnoreCase))
            {
                HandleDeallocate(normalized, session);
                query = new ProtocolCommandQuery(queryText, parametersDataTypes);
                return true;
            }

            return false;
        }

        // Returns true if `normalized` starts with the keyword `word` followed by whitespace or EOL.
        // Prevents `SETTLE` / `BEGIN_FOO` / `COMMITTING` from accidentally matching.
        private static bool StartsWithWord(string normalized, string word)
        {
            if (normalized.StartsWith(word, StringComparison.OrdinalIgnoreCase) == false)
                return false;
            if (normalized.Length == word.Length)
                return true;
            var next = normalized[word.Length];
            return char.IsWhiteSpace(next) || next == ';';
        }

        // Matches `SELECT set_config(...)` (optionally with a FROM/WHERE tail) without going
        // through the full SQL parser.
        private static bool IsSelectSetConfig(string normalized)
        {
            var trimmed = normalized.TrimStart();
            if (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) == false)
                return false;
            var afterSelect = trimmed.AsSpan(6).TrimStart();
            return afterSelect.StartsWith("set_config", StringComparison.OrdinalIgnoreCase);
        }

        private static void HandleDeallocate(string normalized, PgSession session)
        {
            // Expected form: DEALLOCATE "<name>" - the name is the quoted token after the keyword.
            // Both failures below throw PgErrorException (a non-fatal ErrorResponse) rather than a generic
            // exception: in PostgreSQL, deallocating an unknown or unparseable statement is an ordinary
            // client error, so it must surface as an error on the connection - not tear down the session.
            var firstQuote = normalized.IndexOf('"');
            var lastQuote  = normalized.LastIndexOf('"');
            if (firstQuote < 0 || firstQuote == lastQuote)
                throw new PgErrorException(PgErrorCodes.FeatureNotSupported, $"Unsupported DEALLOCATE form (only DEALLOCATE \"<name>\" is supported): {normalized}");

            var statementName = normalized.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
            if (session.NamedStatements.TryRemove(statementName, out var statement) == false)
                throw new PgErrorException(PgErrorCodes.InvalidSqlStatementName, $"prepared statement \"{statementName}\" does not exist");

            // Precaution - the query context should already be disposed by the time we hit DEALLOCATE.
            statement.Dispose();
        }

        public override Task<ICollection<PgColumn>> Init(bool allowMultipleStatements = false)
            => Task.FromResult<ICollection<PgColumn>>(Array.Empty<PgColumn>());

        public override async Task Execute(MessageBuilder builder, PipeWriter writer, CancellationToken token)
        {
            // No data rows - only the CommandComplete tag.
            await writer.WriteAsync(builder.CommandComplete("SELECT 0"), token);
        }

        public override void Dispose()
        {
        }
    }
}
