using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL
{
    /// <summary>
    /// Handles the small set of non-SELECT protocol-level statements that clients send for
    /// connection / session housekeeping rather than data:
    /// <list type="bullet">
    ///   <item><c>DISCARD ALL</c> — resets the session; we have nothing to discard, so the response is empty.</item>
    ///   <item><c>ROLLBACK</c> — no-op for our read-only protocol surface.</item>
    ///   <item><c>DEALLOCATE "&lt;name&gt;"</c> — frees a named prepared statement; removes it from the session.</item>
    /// </list>
    /// These cannot be expressed as SQL/AST shapes so they don't fit the virtual-tables interpreter.
    /// They live in a dedicated dispatch slot at the head of <see cref="PgQuery.CreateInstance"/>.
    /// </summary>
    public sealed class ProtocolCommandQuery : PgQuery
    {
        private const string DiscardAll = "DISCARD ALL";
        private const string Rollback   = "ROLLBACK";
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

            if (normalized.StartsWith(Deallocate, StringComparison.OrdinalIgnoreCase))
            {
                HandleDeallocate(normalized, session);
                query = new ProtocolCommandQuery(queryText, parametersDataTypes);
                return true;
            }

            return false;
        }

        private static void HandleDeallocate(string normalized, PgSession session)
        {
            // Expected form: DEALLOCATE "<name>" — the name is the quoted token after the keyword.
            var firstQuote = normalized.IndexOf('"');
            var lastQuote  = normalized.LastIndexOf('"');
            if (firstQuote < 0 || firstQuote == lastQuote)
                throw new InvalidOperationException($"Unexpected DEALLOCATE syntax (expected quoted name): {normalized}");

            var statementName = normalized.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
            if (session.NamedStatements.TryRemove(statementName, out var statement) == false)
                throw new InvalidOperationException($"Failed to remove prepared statement '{statementName}'");

            // Precaution — the query context should already be disposed by the time we hit DEALLOCATE.
            statement.Dispose();
        }

        public override Task<ICollection<PgColumn>> Init(bool allowMultipleStatements = false)
            => Task.FromResult<ICollection<PgColumn>>(Array.Empty<PgColumn>());

        public override async Task Execute(MessageBuilder builder, PipeWriter writer, CancellationToken token)
        {
            // No data rows — only the CommandComplete tag.
            await writer.WriteAsync(builder.CommandComplete("SELECT 0"), token);
        }

        public override void Dispose()
        {
        }
    }
}
