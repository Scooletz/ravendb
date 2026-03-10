using System;
using System.Threading;
using Raven.Server.Utils.Stats;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace FastTests.Utils;

public class StatsScopeTests(ITestOutputHelper output) : NoDisposalNeeded(output)
{
    private const RavenTestCategory Category = RavenTestCategory.Ai | RavenTestCategory.Etl;

    [RavenFact(Category)]
    public void Duration_Preserved_WhenSetExplicitly()
    {
        TimeSpan duration = TimeSpan.FromDays(1);

        Stats stats = new();

        var scope = new TestStatsScope(stats);
        scope.Duration = duration;
        scope.Dispose();

        Assert.Equal(duration, scope.Duration);
    }

    [RavenFact(Category)]
    public void Duration_Measured_WhenScoped()
    {
        Stats stats = new();

        var scope = new TestStatsScope(stats);
        new SpinWait().SpinOnce();
        scope.Dispose();

        Assert.True(TimeSpan.Zero < scope.Duration && scope.Duration < TimeSpan.FromMilliseconds(1), "Duration should be non-zero and small");
    }

    private sealed class TestStatsScope(Stats stats, bool start = true) :
        StatsScope<Stats, TestStatsScope>(stats, start)
    {
        protected override TestStatsScope OpenNewScope(Stats stats, bool start) => new(stats, start);
    }

    private sealed class Stats;
}
