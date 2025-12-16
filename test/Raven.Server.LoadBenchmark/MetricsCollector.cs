using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HdrHistogram;

namespace Raven.Server.LoadBenchmark
{
    public sealed class MetricsCollector
    {
        // Histogram configuration: track latencies from 1 microsecond to 1 minute with 3 significant digits
        private const long LowestDiscernibleValue = 1;
        private const long HighestTrackableValue = 60_000_000;
        private const int NumberOfSignificantValueDigits = 3;

        // Conversion factor from microseconds to milliseconds
        private const double MicrosecondsToMilliseconds = 1000.0;

        private static readonly double TicksToMicrosecondsRatio = 1_000_000.0 / Stopwatch.Frequency;

        private readonly ThreadLocal<LongHistogram> _threadLocalHistograms;
        private long _successCount;
        private long _failureCount;
        private long _currentInFlight;
        private long _maxInFlight;
        private long _totalInFlightSamples;
        private long _inFlightSampleCount;

        public MetricsCollector()
        {
            _threadLocalHistograms = new ThreadLocal<LongHistogram>(
                () => new LongHistogram(LowestDiscernibleValue, HighestTrackableValue, NumberOfSignificantValueDigits),
                trackAllValues: true);
        }

        public void RecordSuccess(long elapsedTicks)
        {
            var latencyMicroseconds = (long)(elapsedTicks * TicksToMicrosecondsRatio);
            _threadLocalHistograms.Value.RecordValue(Math.Max(1, latencyMicroseconds));
            Interlocked.Increment(ref _successCount);
        }

        public void RecordFailure(Exception ex, long elapsedTicks)
        {
            var latencyMicroseconds = (long)(elapsedTicks * TicksToMicrosecondsRatio);
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
            var mergedHistogram = new LongHistogram(LowestDiscernibleValue, HighestTrackableValue, NumberOfSignificantValueDigits);
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
                P50 = mergedHistogram.GetValueAtPercentile(50) / MicrosecondsToMilliseconds,
                P90 = mergedHistogram.GetValueAtPercentile(90) / MicrosecondsToMilliseconds,
                P95 = mergedHistogram.GetValueAtPercentile(95) / MicrosecondsToMilliseconds,
                P99 = mergedHistogram.GetValueAtPercentile(99) / MicrosecondsToMilliseconds,
                Max = mergedHistogram.GetMaxValue() / MicrosecondsToMilliseconds,
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
