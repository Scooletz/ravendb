using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Server.Utils;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Utils;

public class AsyncBreakpointTests
{
    private const int SpinCount = 10;
    
    [RavenTheory(RavenTestCategory.Replication)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Should_properly_reset_multiple_times_for(int waiterCount)
    {
        AsyncBreakpoint b = new("test", 1);

        for (int i = 0; i < SpinCount; i++)
        {
            Task stop = b.BreakAsync();
            Task waiter = waiterCount == 1 ? Task.Run(() => b.Wait()) : Task.WhenAll(Enumerable.Range(0, waiterCount).Select(_ => Task.Run(() => b.Wait())));
        
            Assert.False(waiter.IsCompleted);
            await stop; // stop should be completed
        
            Assert.False(waiter.IsCompleted);
            await b.ContinueAsync();
            await waiter;
        }
    }
}
