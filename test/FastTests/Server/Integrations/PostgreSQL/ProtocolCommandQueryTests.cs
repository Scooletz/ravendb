using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.Exceptions;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    public sealed class ProtocolCommandQueryTests(ITestOutputHelper output) : NoDisposalNeeded(output)
    {
        // Regression: an unsupported/malformed DEALLOCATE must surface as a non-fatal PgErrorException
        // (a per-statement ErrorResponse), NOT a generic exception that the session loop classifies as
        // fatal and uses to tear down the whole connection. Only the quoted-name form DEALLOCATE "<name>"
        // is parsed; ALL / unquoted / bare forms hit this branch, which throws before touching the
        // session (so a null session is fine here). Deallocating an unknown named statement is the
        // other non-fatal branch, but it requires a live PgSession and is covered by integration tests.
        [RavenTheory(RavenTestCategory.PostgreSql)]
        [InlineData("DEALLOCATE ALL")]
        [InlineData("DEALLOCATE foo")]
        [InlineData("DEALLOCATE")]
        public void Deallocate_unsupportedForm_throwsNonFatalPgError(string sql)
        {
            var ex = Assert.Throws<PgErrorException>(
                () => ProtocolCommandQuery.TryParse(sql, parametersDataTypes: null, session: null, out _));

            Assert.Equal(PgErrorCodes.FeatureNotSupported, ex.ErrorCode);
        }
    }
}
