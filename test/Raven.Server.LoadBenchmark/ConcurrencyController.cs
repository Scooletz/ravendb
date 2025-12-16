using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Server.Routing;
using Raven.Server.Web;

namespace Raven.Server.LoadBenchmark
{
    public sealed class ConcurrencyController
    {
        private readonly RequestRouter _router;
        private readonly RequestContextFactory _contextFactory;

        public ConcurrencyController(RequestRouter router, RequestContextFactory contextFactory)
        {
            _router = router;
            _contextFactory = contextFactory;
        }

        public async Task<MetricsSummary> RunAsync(
            int targetConcurrency,
            TimeSpan warmupDuration,
            TimeSpan measurementDuration,
            bool verbose)
        {
            if (verbose)
                Console.WriteLine($"  Running warmup for {warmupDuration.TotalSeconds}s with concurrency {targetConcurrency}...");

            // Warmup phase
            using (var warmupCts = new CancellationTokenSource(warmupDuration))
            {
                await RunPhaseAsync(targetConcurrency, warmupCts.Token, null);
            }

            if (verbose)
                Console.WriteLine($"  Running measurement for {measurementDuration.TotalSeconds}s with concurrency {targetConcurrency}...");

            // Measurement phase
            var metrics = new MetricsCollector();
            using (var measureCts = new CancellationTokenSource(measurementDuration))
            {
                // Start sampling in-flight count periodically
                var samplingTask = Task.Run(async () =>
                {
                    while (!measureCts.Token.IsCancellationRequested)
                    {
                        metrics.SampleInFlight();
                        try
                        {
                            await Task.Delay(100, measureCts.Token);
                        }
                        catch (TaskCanceledException)
                        {
                            break;
                        }
                    }
                });

                await RunPhaseAsync(targetConcurrency, measureCts.Token, metrics);
                await samplingTask;
            }

            return metrics.GetSummary(measurementDuration);
        }

        private async Task RunPhaseAsync(int targetConcurrency, CancellationToken cancellationToken, MetricsCollector metrics)
        {
            var tasks = new List<Task>();
            for (int i = 0; i < targetConcurrency; i++)
            {
                tasks.Add(WorkerLoopAsync(cancellationToken, metrics));
            }

            await Task.WhenAll(tasks);
        }

        private async Task WorkerLoopAsync(CancellationToken cancellationToken, MetricsCollector metrics)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (metrics != null)
                    metrics.IncrementInFlight();

                var ctx = _contextFactory.CreateContext();
                long startTimestamp = Stopwatch.GetTimestamp();
                try
                {
                    await _router.HandlePath(ctx);
                    long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
                    metrics?.RecordSuccess(elapsed);
                }
                catch (Exception e)
                {
                    long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
                    metrics?.RecordFailure(e, elapsed);
                }
                finally
                {
                    ctx.Dispose();
                    if (metrics != null)
                        metrics.DecrementInFlight();
                }
            }
        }
    }
}
