using Raven.Server.Integrations.PostgreSQL;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    public sealed class RqlIdentifierTests(ITestOutputHelper output) : NoDisposalNeeded(output)
    {
        // Identifier-shaped names are safe to splice into RQL unquoted - including non-ASCII letters, which
        // the RQL scanner accepts (char.IsLetter is Unicode-aware).
        [RavenTheory(RavenTestCategory.PostgreSql)]
        [InlineData("Company")]
        [InlineData("_id")]
        [InlineData("a1")]
        [InlineData("FirstName2")]
        [InlineData("café")]
        [InlineData("Имя")]
        [InlineData("名前")]
        public void Identifier_shaped_names_are_safe(string name) => Assert.True(RqlIdentifier.IsSafe(name));

        // Anything that would need quoting (RQL has no identifier quoting) must be rejected so the caller
        // falls through instead of emitting broken RQL.
        [RavenTheory(RavenTestCategory.PostgreSql)]
        [InlineData("Field With Space")]
        [InlineData("a'b")]
        [InlineData("a\"b")]
        [InlineData("a.b")]
        [InlineData("a-b")]
        [InlineData("1abc")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Names_needing_quoting_are_rejected(string name) => Assert.False(RqlIdentifier.IsSafe(name));
    }
}
