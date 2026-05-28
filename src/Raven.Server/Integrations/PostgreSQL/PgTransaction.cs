using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using NCrontab.Advanced.Extensions;
using Raven.Server.Documents;
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

            // Extended Protocol's Parse message should contain a single statement. Some clients
            // (e.g. Npgsql-based connectors like Microsoft Fabric Copy Job) send a multi-statement
            // batch in one Parse message anyway. We take the last statement in the batch — for
            // Npgsql startup batches (SELECT version(); SELECT <type-discovery>) that is always the
            // meaningful data query. The trivial probes (version, current_setting) are silently
            // dropped; Describe/Execute then serve the real query's schema and rows.
            var stmts = SqlStatementSplitter.Split(cleanQueryText);
            if (stmts.Count > 1)
                cleanQueryText = stmts[^1];

            _currentQuery = PgQuery.CreateInstance(cleanQueryText, parametersDataTypes, DocumentDatabase, Session, Username);
        }

        public void Bind(ICollection<byte[]> parameters, short[] parameterFormatCodes, short[] resultColumnFormatCodes, string statementName = null)
        {
            if (statementName.IsNullOrWhiteSpace() == false)
            {
                State = TransactionState.InTransaction;
                if (Session.NamedStatements.TryGetValue(statementName, out _currentQuery) == false)
                    throw new KeyNotFoundException($"Expected named statement '{statementName}' wasn't found.");
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
    }
}
