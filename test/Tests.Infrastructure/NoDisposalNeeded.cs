using Tests.Infrastructure;
using Xunit.Abstractions;

namespace FastTests
{
    public abstract class NoDisposalNeeded(ITestOutputHelper output) : ParallelTestBase(output);
}
