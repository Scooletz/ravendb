using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Raven.Server.Routing;
using Raven.Server.Web;

namespace Raven.Server.LoadBenchmark
{
    public sealed class RpsController
    {
        private readonly RequestRouter _router;
        private readonly RequestContextFactory _contextFactory;

        public RpsController(RequestRouter router, RequestContextFactory contextFactory)
        {
            _router = router;
            _contextFactory = contextFactory;
        }

        public async Task<MetricsSummary> RunAsync(
            int targetRps,
            TimeSpan warmupDuration,
            TimeSpan measurementDuration,
            bool verbose)
        {
            if (verbose)
                Console.WriteLine($"  Running warmup for {warmupDuration.TotalSeconds}s with target RPS {targetRps}...");

            // Warmup phase
            using (var warmupCts = new CancellationTokenSource(warmupDuration))
            {
                await RunPhaseAsync(targetRps, warmupCts.Token, null);
            }

            if (verbose)
                Console.WriteLine($"  Running measurement for {measurementDuration.TotalSeconds}s with target RPS {targetRps}...");

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

                await RunPhaseAsync(targetRps, measureCts.Token, metrics);
                await samplingTask;
            }

            return metrics.GetSummary(measurementDuration);
        }

        private async Task RunPhaseAsync(int targetRps, CancellationToken cancellationToken, MetricsCollector metrics)
        {
            var intervalMs = 1000.0 / targetRps;
            var sw = Stopwatch.StartNew();
            var nextRequestTime = 0.0;
            var activeTasks = new List<Task>();

            while (!cancellationToken.IsCancellationRequested)
            {
                var now = sw.Elapsed.TotalMilliseconds;
                
                if (now >= nextRequestTime)
                {
                    // Launch a new request
                    var task = ExecuteRequestAsync(metrics);
                    activeTasks.Add(task);
                    
                    nextRequestTime += intervalMs;
                    
                    // Clean up completed tasks periodically
                    if (activeTasks.Count > 1000)
                    {
                        activeTasks.RemoveAll(t => t.IsCompleted);
                    }
                }
                else
                {
                    // Sleep until next scheduled request
                    var sleepMs = Math.Max(0, (int)(nextRequestTime - now));
                    if (sleepMs > 0)
                    {
                        try
                        {
                            await Task.Delay(sleepMs, cancellationToken);
                        }
                        catch (TaskCanceledException)
                        {
                            break;
                        }
                    }
                }
            }

            // Wait for all in-flight requests to complete
            await Task.WhenAll(activeTasks);
        }

        private async Task ExecuteRequestAsync(MetricsCollector metrics)
        {
            if (metrics != null)
                metrics.IncrementInFlight();

            var ctx = _contextFactory.CreateContext();
            var sw = Stopwatch.StartNew();
            try
            {
                await _router.HandlePath(ctx);
                sw.Stop();
                metrics?.RecordSuccess(sw.Elapsed);
            }
            catch (Exception e)
            {
                sw.Stop();
                metrics?.RecordFailure(e, sw.Elapsed);
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
