using System;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.Exceptions;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    public sealed class PgTransactionBindTests(ITestOutputHelper output) : NoDisposalNeeded(output)
    {
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Bind_to_unknown_prepared_statement_is_recoverable_not_fatal()
        {
            var session = new PgSession(client: null, serverCertificateHolder: null, identifier: 0, processId: 0, serverStore: null, token: default);
            using var transaction = new PgTransaction(documentDatabase: null, messageReader: new MessageReader(), username: null, session: session);

            // NamedStatements is empty, so this name resolves to nothing.
            var error = Assert.Throws<PgErrorException>(() => transaction.Bind(
                parameters: Array.Empty<byte[]>(),
                parameterFormatCodes: Array.Empty<short>(),
                resultColumnFormatCodes: Array.Empty<short>(),
                statementName: "statement_the_server_never_prepared"));

            Assert.Equal(PgErrorCodes.InvalidSqlStatementName, error.ErrorCode);
        }
    }
}
