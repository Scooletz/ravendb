using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Raven.Server;
using Raven.Server.Config;
using Raven.Server.Config.Settings;
using Raven.Server.Documents;
using Raven.Server.Utils;
using Sparrow.Logging;

namespace Raven.Server.LoadBenchmark
{
    internal class Program
    {
        private const string DatabaseName = "Benchmark";
        private static RavenServer _server;
        private static DocumentDatabase _database;
        private static IDocumentStore _store;
        private static string _pathToServer;

        static async Task<int> Main(string[] args)
        {
            var config = ParseArguments(args);
            if (config == null)
            {
                PrintUsage();
                return 1;
            }

            Console.WriteLine("=== RavenDB RequestRouter.HandlePath Load Benchmark ===");
            Console.WriteLine();
            Console.WriteLine($"Mode: {config.Mode}");
            Console.WriteLine($"Route: {config.HttpMethod} {config.Path}{config.QueryString}");
            Console.WriteLine($"Load Levels: {string.Join(", ", config.LoadLevels)}");
            Console.WriteLine($"Warmup Duration: {config.WarmupDuration.TotalSeconds}s");
            Console.WriteLine($"Measurement Duration: {config.MeasurementDuration.TotalSeconds}s");
            Console.WriteLine();

            try
            {
                Console.WriteLine("Initializing RavenDB server...");
                await InitializeServerAsync();
                Console.WriteLine("Server initialized successfully.");
                Console.WriteLine();

                var contextFactory = new RequestContextFactory(
                    _server,
                    config.HttpMethod,
                    config.Path,
                    config.QueryString);

                var results = new Dictionary<int, MetricsSummary>();

                if (config.Mode == BenchmarkMode.Concurrency)
                {
                    var controller = new ConcurrencyController(_server.Router, contextFactory);

                    foreach (var concurrency in config.LoadLevels)
                    {
                        Console.WriteLine($"Running concurrency level: {concurrency}");
                        var summary = await controller.RunAsync(
                            concurrency,
                            config.WarmupDuration,
                            config.MeasurementDuration,
                            config.Verbose);

                        results[concurrency] = summary;

                        if (config.Verbose)
                        {
                            Console.WriteLine($"  Completed: {summary.SuccessCount} success, {summary.FailureCount} failures, {summary.AchievedRps:F2} RPS, P95={summary.P95:F2}ms");
                        }
                        Console.WriteLine();
                    }
                }
                else // RPS mode
                {
                    var controller = new RpsController(_server.Router, contextFactory);

                    foreach (var rps in config.LoadLevels)
                    {
                        Console.WriteLine($"Running target RPS: {rps}");
                        var summary = await controller.RunAsync(
                            rps,
                            config.WarmupDuration,
                            config.MeasurementDuration,
                            config.Verbose);

                        results[rps] = summary;

                        if (config.Verbose)
                        {
                            Console.WriteLine($"  Completed: {summary.SuccessCount} success, {summary.FailureCount} failures, {summary.AchievedRps:F2} RPS, P95={summary.P95:F2}ms");
                        }
                        Console.WriteLine();
                    }
                }

                // Analyze and print results
                var kneePoints = ResultsAnalyzer.FindKneePoints(results, config.KneeThreshold, config.MaxErrorRate);
                ResultsAnalyzer.PrintResults(config.Mode, results, kneePoints, config);

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
            finally
            {
                Cleanup();
            }
        }

        private static async Task InitializeServerAsync()
        {
            var configuration = RavenConfiguration.CreateForServer(Guid.NewGuid().ToString());
            configuration.Initialize();

            configuration.Server.Name = "LoadBenchmark";
            configuration.Server.MaxTimeForTaskToWaitForDatabaseToLoad = new TimeSetting(60, TimeUnit.Seconds);
            configuration.Licensing.EulaAccepted = true;
            configuration.Logs.Mode = LogMode.None;
            configuration.Core.RunInMemory = false;
            configuration.Core.ServerUrls = ["http://127.0.0.1:0"];

            _pathToServer = NewDataPath("LoadBenchmark", 0, true);
            configuration.Core.DataDirectory = new PathSetting(_pathToServer);

            _server = new RavenServer(configuration);

            _server.Initialize();
            _server.ServerStore.ValidateFixedPort = false;
            await _server.ServerStore.EnsureNotPassiveAsync();

            var doc = new DatabaseRecord(DatabaseName)
            {
                Settings =
                {
                    [RavenConfiguration.GetKey(x => x.Core.RunInMemory)] = false.ToString(),
                    [RavenConfiguration.GetKey(x => x.Core.ThrowIfAnyIndexCannotBeOpened)] = "true",
                }
            };

            _store = new DocumentStore
            {
                Urls = [_server.WebUrl],
                Database = DatabaseName,
            };
            _store.Initialize();

            // Create database
            _store.Maintenance.Server.Send(new CreateDatabaseOperation(doc));
            _database = await GetDatabaseAsync(_server, DatabaseName);

            // Create a sample document for testing
            using (IDocumentSession session = _store.OpenSession())
            {
                session.Store(new { Name = "TestUser", Email = "test@example.com" }, "users/1-A");
                session.SaveChanges();
            }
        }

        private static async Task<DocumentDatabase> GetDatabaseAsync(RavenServer ravenServer, string databaseName)
        {
            var database = await ravenServer.ServerStore.DatabasesLandlord.TryGetOrCreateResourceStore(databaseName).ConfigureAwait(false);
            if (database != null)
                return database;

            throw new InvalidOperationException($"Failed to get or create database: {databaseName}");
        }

        private static string NewDataPath(string testName, int serverPort, bool forceCreateDir = false)
        {
            testName = testName?.Replace("<", "").Replace(">", "");

            var newDataDir = Path.GetFullPath($".{Path.DirectorySeparatorChar}Databases{Path.DirectorySeparatorChar}{testName ?? "TestDatabase"}.{serverPort}-{Guid.NewGuid()}");

            if (forceCreateDir && Directory.Exists(newDataDir) == false)
                Directory.CreateDirectory(newDataDir);

            return newDataDir;
        }

        private static void Cleanup()
        {
            Console.WriteLine("Cleaning up...");

            try
            {
                _store?.Dispose();
            }
            catch { }

            try
            {
                _server?.Dispose();
            }
            catch { }

            if (!string.IsNullOrEmpty(_pathToServer))
            {
                try
                {
                    DeletePath(_pathToServer);
                }
                catch { }
            }
        }

        private static void DeletePath(string pathToDelete)
        {
            if (Directory.Exists(pathToDelete))
            {
                var isRetry = false;
                while (true)
                {
                    try
                    {
                        Directory.Delete(pathToDelete, true);
                        break;
                    }
                    catch (IOException)
                    {
                        if (isRetry)
                            throw;

                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        isRetry = true;
                        Thread.Sleep(200);
                    }
                }
            }
        }

        private static BenchmarkConfig ParseArguments(string[] args)
        {
            var config = new BenchmarkConfig();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--mode":
                    case "-m":
                        if (i + 1 < args.Length)
                        {
                            config.Mode = args[++i].ToLowerInvariant() switch
                            {
                                "concurrency" => BenchmarkMode.Concurrency,
                                "rps" => BenchmarkMode.Rps,
                                _ => throw new ArgumentException($"Invalid mode: {args[i]}")
                            };
                        }
                        break;

                    case "--method":
                        if (i + 1 < args.Length)
                            config.HttpMethod = args[++i];
                        break;

                    case "--path":
                    case "-p":
                        if (i + 1 < args.Length)
                            config.Path = args[++i];
                        break;

                    case "--query":
                    case "-q":
                        if (i + 1 < args.Length)
                            config.QueryString = args[++i];
                        break;

                    case "--levels":
                    case "-l":
                        if (i + 1 < args.Length)
                        {
                            var levelsStr = args[++i];
                            config.LoadLevels = levelsStr.Split(',')
                                .Select(s => int.Parse(s.Trim()))
                                .ToArray();
                        }
                        break;

                    case "--warmup":
                    case "-w":
                        if (i + 1 < args.Length)
                            config.WarmupDuration = TimeSpan.FromSeconds(double.Parse(args[++i]));
                        break;

                    case "--duration":
                    case "-d":
                        if (i + 1 < args.Length)
                            config.MeasurementDuration = TimeSpan.FromSeconds(double.Parse(args[++i]));
                        break;

                    case "--knee-threshold":
                    case "-k":
                        if (i + 1 < args.Length)
                            config.KneeThreshold = double.Parse(args[++i]);
                        break;

                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length)
                            config.OutputCsvPath = args[++i];
                        break;

                    case "--verbose":
                    case "-v":
                        config.Verbose = true;
                        break;

                    case "--help":
                    case "-h":
                        return null;
                }
            }

            return config;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: Raven.Server.LoadBenchmark [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --mode, -m <concurrency|rps>    Benchmark mode (default: concurrency)");
            Console.WriteLine("  --method <GET|POST|...>          HTTP method (default: GET)");
            Console.WriteLine("  --path, -p <path>                Request path (default: /databases/Benchmark/docs)");
            Console.WriteLine("  --query, -q <query>              Query string (default: ?id=users/1-A)");
            Console.WriteLine("  --levels, -l <1,2,4,8,...>       Comma-separated load levels (default: 1,2,4,8,16,32,64)");
            Console.WriteLine("  --warmup, -w <seconds>           Warmup duration in seconds (default: 5)");
            Console.WriteLine("  --duration, -d <seconds>         Measurement duration in seconds (default: 10)");
            Console.WriteLine("  --knee-threshold, -k <value>     Knee detection threshold (default: 3.0)");
            Console.WriteLine("  --output, -o <file.csv>          Export results to CSV file");
            Console.WriteLine("  --verbose, -v                    Verbose output");
            Console.WriteLine("  --help, -h                       Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  # Run concurrency mode with default settings");
            Console.WriteLine("  dotnet run --project test/Raven.Server.LoadBenchmark");
            Console.WriteLine();
            Console.WriteLine("  # Run RPS mode with custom levels");
            Console.WriteLine("  dotnet run --project test/Raven.Server.LoadBenchmark -- --mode rps --levels 10,50,100,200");
            Console.WriteLine();
            Console.WriteLine("  # Run with custom path and export to CSV");
            Console.WriteLine("  dotnet run --project test/Raven.Server.LoadBenchmark -- --path /databases/Benchmark/indexes --output results.csv");
        }
    }
}
