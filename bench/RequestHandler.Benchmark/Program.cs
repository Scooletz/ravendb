using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace RequestHandler.Benchmark;

internal static class Program
{
    private static void Main(string[] args)
    {
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
