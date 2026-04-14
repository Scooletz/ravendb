using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Npgsql;
using Raven.Server.Integrations.PostgreSQL.PowerBI;

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

            if (PowerBIHardcodedAstMatcher.TryMatchPowerBIHardcodedQuery(queryText, out result))
            {
                hardcodedQuery = new HardcodedQuery(queryText, parametersDataTypes, result);
                return true;
            }

            // Simple Npgsql function queries — AST-based (version(), current_setting('max_index_keys')).
            // These are structurally trivial and safe to match via AST; tolerates whitespace variations.
            if (NpgsqlSimpleQueryAstMatcher.TryMatch(queryText, out result))
            {
                hardcodedQuery = new HardcodedQuery(queryText, parametersDataTypes, result);
                return true;
            }

            // Npgsql enum/composite metadata queries — AST-based.
            // All version variants (4.0.0–current) differ only in comment style or ORDER BY column
            // but return identical response schemas, so a single AST matcher covers all versions.
            if (NpgsqlMetadataQueryAstMatcher.TryMatch(queryText, out result))
            {
                hardcodedQuery = new HardcodedQuery(queryText, parametersDataTypes, result);
                return true;
            }

            // Covers all supported Npgsql versions (3.x through 5.x+).
            // See NpgsqlTypesQueryAstMatcher for per-family details.
            if (NpgsqlTypesQueryAstMatcher.TryMatch(queryText, out result))
            {
                hardcodedQuery = new HardcodedQuery(queryText, parametersDataTypes, result);
                return true;
            }

            if (normalizedQuery.StartsWith("DISCARD ALL", StringComparison.OrdinalIgnoreCase))
                result = new PgTable();

            else if (normalizedQuery.StartsWith("ROLLBACK", StringComparison.OrdinalIgnoreCase))
                result = new PgTable();

            else if (normalizedQuery.StartsWith("DEALLOCATE", StringComparison.OrdinalIgnoreCase))
            {
                var statementName = normalizedQuery.Split("\"")[1];
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
