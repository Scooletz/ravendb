using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using NCrontab.Advanced.Extensions;
using Raven.Server.Documents;
using Raven.Server.Integrations.PostgreSQL.Exceptions;
using Raven.Server.Integrations.PostgreSQL.Messages;

namespace Raven.Server.Integrations.PostgreSQL
{
    public enum TransactionState : byte
    {
        Idle = (byte)'I',
        InTransaction = (byte)'T',
        Failed = (byte)'E'
    }

    public sealed class PgTransaction : IDisposable
    {
        public TransactionState State { get; private set; } = TransactionState.Idle;
        public DocumentDatabase DocumentDatabase { get; }
        public MessageReader MessageReader { get; private set; }
        public string Username { get; private set; }
        
        internal PgQuery _currentQuery;
        internal PgSession Session { get; init; }
        
        public PgTransaction(DocumentDatabase documentDatabase, MessageReader messageReader, string username, PgSession session)
        {
            DocumentDatabase = documentDatabase;
            MessageReader = messageReader;
            Username = username;
            Session = session;
        }

        public void Init(string cleanQueryText, int[] parametersDataTypes)
        {
            State = TransactionState.InTransaction;

            MessageReader?.Dispose();
            MessageReader = new MessageReader();

            _currentQuery?.Dispose();

            // Extended Protocol Parse should carry one statement, but some clients (e.g. Microsoft
            // Fabric's Copy Job) send a multi-statement batch. We keep the last statement and drop the
            // leading ones only if they're known startup-probe trivia; otherwise refuse.
            var stmts = SqlStatementSplitter.Split(cleanQueryText);
            if (stmts.Count > 1)
            {
                for (int i = 0; i < stmts.Count - 1; i++)
                {
                    if (IsTriviaStatement(stmts[i]) == false)
                    {
                        throw new PgErrorException(
                            PgErrorCodes.FeatureNotSupported,
                            $"Extended Protocol Parse received {stmts.Count} statements, but only the last is served and the leading ones must be startup-probe trivia (SET / SHOW / RESET / BEGIN / COMMIT / ROLLBACK / SELECT version() / SELECT current_setting). Got a non-trivia leading statement: {stmts[i]}");
                    }
                }
                cleanQueryText = stmts[^1];
            }

            _currentQuery = PgQuery.CreateInstance(cleanQueryText, parametersDataTypes, DocumentDatabase, Session, Username);
        }

        public void Bind(ICollection<byte[]> parameters, short[] parameterFormatCodes, short[] resultColumnFormatCodes, string statementName = null)
        {
            if (statementName.IsNullOrWhiteSpace() == false)
            {
                State = TransactionState.InTransaction;
                if (Session.NamedStatements.TryGetValue(statementName, out _currentQuery) == false)
                    throw new PgErrorException(PgErrorCodes.InvalidSqlStatementName, $"prepared statement \"{statementName}\" does not exist");
            }
            _currentQuery.Bind(parameters, parameterFormatCodes, resultColumnFormatCodes);
        }

        public async Task<(ICollection<PgColumn> schema, int[] parameterDataTypes)> Describe()
        {
            return (await _currentQuery.Init(), _currentQuery.ParametersDataTypes);
        }

        public async Task Execute(MessageBuilder messageBuilder, PipeWriter writer, CancellationToken token)
        {
            await _currentQuery.Execute(messageBuilder, writer, token);
        }

        public void Fail()
        {
            State = TransactionState.Failed;
        }

        public void Close()
        {
            State = TransactionState.Idle;

            _currentQuery?.Dispose();
            _currentQuery = null;
        }

        public void Sync()
        {
            State = TransactionState.Idle;
            _currentQuery?.Dispose();
            _currentQuery = null;
        }

        public void Dispose()
        {
            _currentQuery?.Dispose();
            _currentQuery = null;

            MessageReader?.Dispose();
            MessageReader = null;
        }

        // Statement shapes safe to drop from a multi-statement Parse: startup-probe trivia
        // (Npgsql/Fabric/pgAdmin) with no useful data. Anything else might be real work, so refuse.
        internal static bool IsTriviaStatement(string stmt)
        {
            if (string.IsNullOrWhiteSpace(stmt))
                return true;

            var span = stmt.AsSpan().TrimStart();

            if (StartsWithKeyword(span, "set") ||
                StartsWithKeyword(span, "show") ||
                StartsWithKeyword(span, "reset") ||
                StartsWithKeyword(span, "begin") ||
                StartsWithKeyword(span, "commit") ||
                StartsWithKeyword(span, "rollback") ||
                StartsWithKeyword(span, "end") ||
                StartsWithKeyword(span, "start"))
                return true;

            // Allow Npgsql version probe (`SELECT version()`) and current_setting probes.
            if (StartsWithKeyword(span, "select"))
            {
                var rest = span[6..].TrimStart();
                if (rest.StartsWith("version", StringComparison.OrdinalIgnoreCase) ||
                    rest.StartsWith("current_setting", StringComparison.OrdinalIgnoreCase) ||
                    rest.StartsWith("pg_catalog.set_config", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool StartsWithKeyword(ReadOnlySpan<char> span, string keyword)
        {
            if (span.Length < keyword.Length)
                return false;
            for (int i = 0; i < keyword.Length; i++)
            {
                if (char.ToLowerInvariant(span[i]) != keyword[i])
                    return false;
            }
            if (span.Length == keyword.Length)
                return true;
            var next = span[keyword.Length];
            // Keyword must be followed by a word boundary (whitespace, end-of-string, or punctuation).
            return char.IsLetterOrDigit(next) == false && next != '_';
        }
    }
}
