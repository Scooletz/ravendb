using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Raven.Server.Integrations.PostgreSQL.Classification;
using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL
{
    public sealed class HardcodedQuery : PgQuery
    {
        private PgTable _result;

        public HardcodedQuery(string queryString, int[] parametersDataTypes, PgTable result) : base(queryString, parametersDataTypes)
        {
            _result = result;
        }

        public static bool TryParse(string queryText, int[] parametersDataTypes, PgSession session, out HardcodedQuery hardcodedQuery)
        {
            var normalizedQuery = queryText.NormalizeLineEndings();
            PgTable result = null;

            // AST-based recognition: parse once, ask each classifier "do you recognize this
            // shape?", and use the canonical PgTable response it returns. Covers both PowerBI
            // and Npgsql metadata/initialization queries.
            if (HardcodedQueryClassifier.TryClassify(queryText, out result))
            {
                hardcodedQuery = new HardcodedQuery(queryText, parametersDataTypes, result);
                return true;
            }

            // Protocol-level no-op statements — handled here, not via intent recognition,
            // because they are not SELECTs and have no AST-level "metadata" shape.
            if (normalizedQuery.StartsWith("DISCARD ALL", StringComparison.OrdinalIgnoreCase))
                result = new PgTable();

            else if (normalizedQuery.StartsWith("ROLLBACK", StringComparison.OrdinalIgnoreCase))
                result = new PgTable();

            else if (normalizedQuery.StartsWith("DEALLOCATE", StringComparison.OrdinalIgnoreCase))
            {
                // Expected form: DEALLOCATE "<name>"
                // Extract the name as the substring between the first and last double-quote.
                var firstQuote = normalizedQuery.IndexOf('"');
                var lastQuote  = normalizedQuery.LastIndexOf('"');
                if (firstQuote < 0 || firstQuote == lastQuote)
                    throw new InvalidOperationException($"Unexpected DEALLOCATE syntax (expected quoted name): {normalizedQuery}");

                var statementName = normalizedQuery.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                if (session.NamedStatements.TryRemove(statementName, out var statement) == false)
                    throw new InvalidOperationException($"Failed to remove prepared statement '{statementName}'");
                statement.Dispose(); // precaution - query context should be already disposed
                result = new PgTable();
            }
                
            if (result != null)
            {
                hardcodedQuery = new HardcodedQuery(queryText, parametersDataTypes, result);
                return true;
            }

            hardcodedQuery = null;
            return false;
        }

        public override Task<ICollection<PgColumn>> Init(bool allowMultipleStatements = false)
        {
            if (IsEmptyQuery)
                return Task.FromResult<ICollection<PgColumn>>(null);

            if (_result != null)
                return Task.FromResult<ICollection<PgColumn>>(_result.Columns);

            return Task.FromResult<ICollection<PgColumn>>(Array.Empty<PgColumn>());
        }

        public override async Task Execute(MessageBuilder builder, PipeWriter writer, CancellationToken token)
        {
            if (_result?.Data != null)
            {
                foreach (var dataRow in _result.Data)
                {
                    await writer.WriteAsync(builder.DataRow(dataRow.ColumnData.Span), token);
                }
            }

            await writer.WriteAsync(builder.CommandComplete($"SELECT {_result?.Data?.Count ?? 0}"), token);
        }

        public override void Dispose()
        {
        }
    }
}
