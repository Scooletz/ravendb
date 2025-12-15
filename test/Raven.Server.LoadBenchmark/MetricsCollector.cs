using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Raven.Server.LoadBenchmark
{
    public sealed class MetricsCollector
    {
        private readonly List<double> _latenciesMs = new();
        private readonly object _lock = new();
        private long _successCount;
        private long _failureCount;
        private long _currentInFlight;
        private long _maxInFlight;
        private long _totalInFlightSamples;
        private long _inFlightSampleCount;

        public void RecordSuccess(TimeSpan elapsed)
        {
            var latencyMs = elapsed.TotalMilliseconds;
            lock (_lock)
            {
                _latenciesMs.Add(latencyMs);
                Interlocked.Increment(ref _successCount);
            }
        }

        public void RecordFailure(Exception ex, TimeSpan elapsed)
        {
            var latencyMs = elapsed.TotalMilliseconds;
            lock (_lock)
            {
                _latenciesMs.Add(latencyMs);
                Interlocked.Increment(ref _failureCount);
            }
        }

        public void IncrementInFlight()
        {
            var current = Interlocked.Increment(ref _currentInFlight);
            
            // Update max
            long max;
            do
            {
                max = Interlocked.Read(ref _maxInFlight);
                if (current <= max)
                    break;
            } while (Interlocked.CompareExchange(ref _maxInFlight, current, max) != max);
        }

        public void DecrementInFlight()
        {
            Interlocked.Decrement(ref _currentInFlight);
        }

        public void SampleInFlight()
        {
            var current = Interlocked.Read(ref _currentInFlight);
            Interlocked.Add(ref _totalInFlightSamples, current);
            Interlocked.Increment(ref _inFlightSampleCount);
        }

        public MetricsSummary GetSummary(TimeSpan measurementDuration)
        {
            lock (_lock)
            {
                var sortedLatencies = _latenciesMs.OrderBy(x => x).ToArray();
                var totalRequests = _successCount + _failureCount;
                
                return new MetricsSummary
                {
                    TotalRequests = totalRequests,
                    SuccessCount = _successCount,
                    FailureCount = _failureCount,
                    ErrorRate = totalRequests > 0 ? (double)_failureCount / totalRequests : 0,
                    AchievedRps = totalRequests / measurementDuration.TotalSeconds,
                    P50 = GetPercentile(sortedLatencies, 0.50),
                    P90 = GetPercentile(sortedLatencies, 0.90),
                    P95 = GetPercentile(sortedLatencies, 0.95),
                    P99 = GetPercentile(sortedLatencies, 0.99),
                    Max = sortedLatencies.Length > 0 ? sortedLatencies[^1] : 0,
                    AvgInFlight = _inFlightSampleCount > 0 ? (double)_totalInFlightSamples / _inFlightSampleCount : 0,
                    MaxInFlight = _maxInFlight
                };
            }
        }

        private static double GetPercentile(double[] sortedValues, double percentile)
        {
            if (sortedValues.Length == 0)
                return 0;

            var index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
            index = Math.Max(0, Math.Min(index, sortedValues.Length - 1));
            return sortedValues[index];
        }
    }

    public sealed class MetricsSummary
    {
        public long TotalRequests { get; init; }
        public long SuccessCount { get; init; }
        public long FailureCount { get; init; }
        public double ErrorRate { get; init; }
        public double AchievedRps { get; init; }
        public double P50 { get; init; }
        public double P90 { get; init; }
        public double P95 { get; init; }
        public double P99 { get; init; }
        public double Max { get; init; }
        public double AvgInFlight { get; init; }
        public long MaxInFlight { get; init; }
    }
}
