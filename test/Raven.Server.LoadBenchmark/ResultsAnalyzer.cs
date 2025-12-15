using System;
using System.Collections.Generic;
using System.Linq;

namespace Raven.Server.LoadBenchmark
{
    public sealed class ResultsAnalyzer
    {
        public static List<int> FindKneePoints(
            Dictionary<int, MetricsSummary> results,
            double kneeThreshold,
            double maxErrorRate)
        {
            var kneePoints = new List<int>();
            var sortedLevels = results.Keys.OrderBy(x => x).ToArray();

            if (sortedLevels.Length < 3)
                return kneePoints;

            for (int i = 1; i < sortedLevels.Length - 1; i++)
            {
                var prevLevel = sortedLevels[i - 1];
                var currLevel = sortedLevels[i];
                var nextLevel = sortedLevels[i + 1];

                var prevSummary = results[prevLevel];
                var currSummary = results[currLevel];
                var nextSummary = results[nextLevel];

                // Skip if error rate is too high
                if (currSummary.ErrorRate > maxErrorRate)
                    continue;

                var p95Prev = prevSummary.P95;
                var p95Curr = currSummary.P95;
                var p95Next = nextSummary.P95;

                // Calculate slopes
                var slope1 = (p95Curr - p95Prev) / (currLevel - prevLevel);
                var slope2 = (p95Next - p95Curr) / (nextLevel - currLevel);

                // Detect knee: slope2 is significantly larger than slope1
                if (slope1 > 0 && slope2 / slope1 >= kneeThreshold)
                {
                    kneePoints.Add(currLevel);
                }
            }

            return kneePoints;
        }

        public static void PrintResults(
            BenchmarkMode mode,
            Dictionary<int, MetricsSummary> results,
            List<int> kneePoints,
            BenchmarkConfig config)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {mode} Mode Results ===");
            Console.WriteLine();

            var loadLabel = mode == BenchmarkMode.Concurrency ? "Concurrency" : "Target RPS";

            // Header
            Console.WriteLine($"{"Load Level",-12} {"P50 (ms)",-10} {"P90 (ms)",-10} {"P95 (ms)",-10} {"P99 (ms)",-10} {"Max (ms)",-10} {"Error %",-10} {"Achieved RPS",-15} {"Avg InFlight",-15} {"Max InFlight",-15}");
            Console.WriteLine(new string('-', 145));

            // Data rows
            foreach (var level in results.Keys.OrderBy(x => x))
            {
                var summary = results[level];
                var kneeMarker = kneePoints.Contains(level) ? " <- KNEE" : "";
                
                Console.WriteLine(
                    $"{level,-12} " +
                    $"{summary.P50,-10:F2} " +
                    $"{summary.P90,-10:F2} " +
                    $"{summary.P95,-10:F2} " +
                    $"{summary.P99,-10:F2} " +
                    $"{summary.Max,-10:F2} " +
                    $"{summary.ErrorRate * 100,-10:F2} " +
                    $"{summary.AchievedRps,-15:F2} " +
                    $"{summary.AvgInFlight,-15:F2} " +
                    $"{summary.MaxInFlight,-15}" +
                    kneeMarker);
            }

            Console.WriteLine();
            if (kneePoints.Count > 0)
            {
                Console.WriteLine($"Detected knee point(s) at {loadLabel}: {string.Join(", ", kneePoints)}");
            }
            else
            {
                Console.WriteLine("No clear knee point detected in the tested range.");
            }
            Console.WriteLine();

            // Export to CSV if requested
            if (!string.IsNullOrEmpty(config.OutputCsvPath))
            {
                ExportToCsv(mode, results, config.OutputCsvPath);
                Console.WriteLine($"Results exported to: {config.OutputCsvPath}");
                Console.WriteLine();
            }
        }

        private static void ExportToCsv(BenchmarkMode mode, Dictionary<int, MetricsSummary> results, string filePath)
        {
            using var writer = new System.IO.StreamWriter(filePath);
            
            // Header
            writer.WriteLine("LoadLevel,P50,P90,P95,P99,Max,ErrorRate,AchievedRps,AvgInFlight,MaxInFlight");

            // Data
            foreach (var level in results.Keys.OrderBy(x => x))
            {
                var s = results[level];
                writer.WriteLine($"{level},{s.P50},{s.P90},{s.P95},{s.P99},{s.Max},{s.ErrorRate},{s.AchievedRps},{s.AvgInFlight},{s.MaxInFlight}");
            }
        }
    }
}
