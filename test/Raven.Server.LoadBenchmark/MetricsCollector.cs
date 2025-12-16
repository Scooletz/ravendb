using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HdrHistogram;

namespace Raven.Server.LoadBenchmark
{
    public sealed class MetricsCollector
    {
        private readonly ThreadLocal<LongHistogram> _threadLocalHistograms;
        private readonly double _ticksToMicrosecondsRatio;
        private long _successCount;
        private long _failureCount;
        private long _currentInFlight;
        private long _maxInFlight;
        private long _totalInFlightSamples;
        private long _inFlightSampleCount;

        public MetricsCollector()
        {
            // Initialize thread-local histograms
            // Track latencies from 1 microsecond to 1 minute with 3 significant digits
            _threadLocalHistograms = new ThreadLocal<LongHistogram>(
                () => new LongHistogram(1, 60_000_000, 3),
                trackAllValues: true);

            // Calculate the conversion ratio from ticks to microseconds
            _ticksToMicrosecondsRatio = 1_000_000.0 / Stopwatch.Frequency;
        }

        public void RecordSuccess(long elapsedTicks)
        {
            var latencyMicroseconds = (long)(elapsedTicks * _ticksToMicrosecondsRatio);
            _threadLocalHistograms.Value.RecordValue(Math.Max(1, latencyMicroseconds));
            Interlocked.Increment(ref _successCount);
        }

        public void RecordFailure(Exception ex, long elapsedTicks)
        {
            var latencyMicroseconds = (long)(elapsedTicks * _ticksToMicrosecondsRatio);
            _threadLocalHistograms.Value.RecordValue(Math.Max(1, latencyMicroseconds));
            Interlocked.Increment(ref _failureCount);
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
            // Merge all thread-local histograms
            var mergedHistogram = new LongHistogram(1, 60_000_000, 3);
            foreach (var histogram in _threadLocalHistograms.Values)
            {
                mergedHistogram.Add(histogram);
            }

            var totalRequests = _successCount + _failureCount;

            return new MetricsSummary
            {
                TotalRequests = totalRequests,
                SuccessCount = _successCount,
                FailureCount = _failureCount,
                ErrorRate = totalRequests > 0 ? (double)_failureCount / totalRequests : 0,
                AchievedRps = totalRequests / measurementDuration.TotalSeconds,
                P50 = mergedHistogram.GetValueAtPercentile(50) / 1000.0, // Convert to milliseconds
                P90 = mergedHistogram.GetValueAtPercentile(90) / 1000.0,
                P95 = mergedHistogram.GetValueAtPercentile(95) / 1000.0,
                P99 = mergedHistogram.GetValueAtPercentile(99) / 1000.0,
                Max = mergedHistogram.GetMaxValue() / 1000.0,
                AvgInFlight = _inFlightSampleCount > 0 ? (double)_totalInFlightSamples / _inFlightSampleCount : 0,
                MaxInFlight = _maxInFlight
            };
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
