using System;

namespace Raven.Server.LoadBenchmark
{
    public sealed class BenchmarkConfig
    {
        public BenchmarkMode Mode { get; set; } = BenchmarkMode.Concurrency;
        public string HttpMethod { get; set; } = "GET";
        public string Path { get; set; } = "/databases/Benchmark/docs";
        public string QueryString { get; set; } = "?id=users/1-A";
        public int[] LoadLevels { get; set; } = [1, 2, 4, 8, 16, 32, 64];
        public TimeSpan WarmupDuration { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan MeasurementDuration { get; set; } = TimeSpan.FromSeconds(10);
        public double KneeThreshold { get; set; } = 3.0; // Slope multiplier threshold
        public double MaxErrorRate { get; set; } = 0.05; // 5% max error rate for knee detection
        public string OutputCsvPath { get; set; }
        public bool Verbose { get; set; } = false;
    }

    public enum BenchmarkMode
    {
        Concurrency,
        Rps
    }
}
