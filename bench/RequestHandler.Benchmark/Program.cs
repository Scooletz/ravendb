using System;
using System.Globalization;
using System.Threading.Tasks;

namespace RequestHandler.Benchmark;

internal static class Program
{
    private const int DefaultIterations = 10_000;

    private static async Task<int> Main(string[] args)
    {
        var (iterations, useRequestScope) = ParseArguments(args);

        Console.WriteLine($"Starting RequestHandler harness with {iterations:N0} iterations (request scope: {useRequestScope}).");

        await using var harness = await RequestHandlerHarness.CreateAsync(useRequestScope);

        await harness.WarmupAsync();

        var (elapsed, failures, lastFailure) = await harness.ExecuteAsync(iterations);

        Console.WriteLine($"Completed {iterations:N0} iterations in {elapsed}. Failures: {failures:N0}.");
        if (failures > 0)
        {
            if (lastFailure != null)
                Console.WriteLine($"Last failure: {lastFailure}");
            return 1;
        }

        var perRequest = elapsed.TotalMilliseconds / iterations;
        Console.WriteLine($"Average per request: {perRequest:F3} ms");
        return 0;
    }

    private static (int Iterations, bool UseRequestScope) ParseArguments(string[] args)
    {
        var iterations = DefaultIterations;
        var useRequestScope = false;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--iterations=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[("--iterations=").Length..];
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                    iterations = parsed;
                continue;
            }

            if (string.Equals(arg, "--use-request-scope", StringComparison.OrdinalIgnoreCase))
            {
                useRequestScope = true;
            }
        }

        return (iterations, useRequestScope);
    }
}
