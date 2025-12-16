# RavenDB RequestRouter.HandlePath Load Benchmark

This is an in-process load benchmark tool for testing `RequestRouter.HandlePath` performance without using HTTP clients, sockets, or Kestrel. The benchmark directly constructs `HttpContext` and `RequestHandlerContext` objects and calls `HandlePath` to stress-test the routing and request handling logic.

## Features

- **Two Load Control Modes:**
  - **Concurrency Mode**: Maintains a fixed number of concurrent requests
  - **RPS Mode**: Targets a specific requests-per-second rate

- **Comprehensive Metrics Collection:**
  - Latency percentiles (P50, P90, P95, P99, Max) using HdrHistogram for accurate measurements
  - High-resolution timing with `Stopwatch.GetTimestamp()`
  - Thread-local histograms for lock-free recording
  - Error rates
  - Achieved throughput (RPS)
  - In-flight request tracking

- **Automatic Knee/Elbow Detection:**
  - Analyzes latency curves to identify performance degradation points
  - Uses configurable slope-change heuristic on P95 latency

- **Flexible Configuration:**
  - Configurable HTTP method, path, and query string
  - Adjustable load levels, warmup, and measurement durations
  - CSV export for external analysis

## Building

```bash
cd /path/to/ravendb
dotnet build test/Raven.Server.LoadBenchmark/Raven.Server.LoadBenchmark.csproj -c Release
```

## Usage

### Basic Usage (Default Settings)

```bash
dotnet run --project test/Raven.Server.LoadBenchmark/Raven.Server.LoadBenchmark.csproj -c Release
```

This runs concurrency mode with levels `1,2,4,8,16,32,64`, 5-second warmup, and 10-second measurement per level.

### Command-Line Options

```
Options:
  --mode, -m <concurrency|rps>    Benchmark mode (default: concurrency)
  --method <GET|POST|...>          HTTP method (default: GET)
  --path, -p <path>                Request path (default: /databases/Benchmark/docs)
  --query, -q <query>              Query string (default: ?id=users/1-A)
  --levels, -l <1,2,4,8,...>       Comma-separated load levels (default: 1,2,4,8,16,32,64)
  --warmup, -w <seconds>           Warmup duration in seconds (default: 5)
  --duration, -d <seconds>         Measurement duration in seconds (default: 10)
  --knee-threshold, -k <value>     Knee detection threshold (default: 3.0)
  --output, -o <file.csv>          Export results to CSV file
  --verbose, -v                    Verbose output
  --help, -h                       Show this help message
```

### Examples

#### Run Concurrency Mode with Default Settings

```bash
dotnet run --project test/Raven.Server.LoadBenchmark/Raven.Server.LoadBenchmark.csproj -c Release
```

#### Run RPS Mode with Custom Levels

```bash
dotnet run --project test/Raven.Server.LoadBenchmark/Raven.Server.LoadBenchmark.csproj -c Release -- \
  --mode rps \
  --levels 10,50,100,200,500,1000
```

#### Test a Different Endpoint with CSV Export

```bash
dotnet run --project test/Raven.Server.LoadBenchmark/Raven.Server.LoadBenchmark.csproj -c Release -- \
  --path /databases/Benchmark/indexes \
  --query "" \
  --output results.csv
```

#### Run with Verbose Output

```bash
dotnet run --project test/Raven.Server.LoadBenchmark/Raven.Server.LoadBenchmark.csproj -c Release -- \
  --verbose \
  --levels 1,2,4,8 \
  --warmup 2 \
  --duration 5
```

## Understanding the Output

The benchmark prints a table with metrics for each load level:

```
Load Level   P50 (ms)   P90 (ms)   P95 (ms)   P99 (ms)   Max (ms)   Error %    Achieved RPS    Avg InFlight    Max InFlight
-------------------------------------------------------------------------------------------------------------------------------------------------
1            0.05       0.05       0.06       0.08       9.27       0.00       17376.50        1.00            1
2            0.01       0.01       0.01       0.01       9.54       0.00       86422.50        1.00            1
4            0.01       0.01       0.01       0.01       8.81       0.00       87034.50        1.00            1
8            0.01       0.01       0.01       0.01       9.28       0.00       88134.00        1.00            1  <- KNEE
```

- **Load Level**: The concurrency level (concurrent requests) or target RPS
- **P50/P90/P95/P99**: Latency percentiles in milliseconds
- **Max**: Maximum observed latency
- **Error %**: Percentage of failed requests
- **Achieved RPS**: Actual requests per second achieved
- **Avg/Max InFlight**: Average and maximum number of concurrent requests (most useful in RPS mode)
- **<- KNEE**: Marks detected performance degradation points

## Knee Detection

The benchmark uses a simple heuristic to detect when latency starts to increase significantly:

1. For each triple of consecutive load levels, it calculates the slope of P95 latency
2. If the second slope is significantly larger than the first (by default, 3× or more), it marks a knee
3. Only levels with error rates below the threshold (default 5%) are considered

This helps identify the "sweet spot" where increasing load starts to degrade performance.

## Architecture

### Key Components

- **`MetricsCollector`**: Tracks per-request latencies using HdrHistogram with thread-local instances for lock-free recording, errors, and in-flight counts
- **`ConcurrencyController`**: Maintains fixed concurrent request count
- **`RpsController`**: Schedules requests at target RPS rate
- **`RequestContextFactory`**: Creates synthetic `HttpContext` and `RequestHandlerContext`
- **`ResultsAnalyzer`**: Performs knee detection and formats output

### Request Flow

1. `RequestContextFactory.CreateContext()` builds a `DefaultHttpContext` with:
   - Request method, path, query string
   - Request and response bodies (memory streams)
   - HTTP connection features (IP addresses, ports)

2. The controller calls `RequestRouter.HandlePath(context)` directly

3. Metrics are collected for each request:
   - Start timestamp using `Stopwatch.GetTimestamp()`
   - Success or failure
   - Elapsed time in ticks, converted to microseconds and recorded in thread-local HdrHistogram
   - Current in-flight count

4. After all load levels complete, thread-local histograms are merged and results are analyzed and printed

## Notes

- The benchmark spins up a real `RavenServer` instance with a test database
- A sample document (`users/1-A`) is created for testing
- The server and database are cleaned up automatically on exit
- This is designed for local performance testing, not CI/CD
- Results may vary based on hardware, OS, and other running processes

## Limitations

- Does not test actual HTTP/network stack performance
- Does not simulate realistic client connection patterns
- Single-machine only (no distributed scenarios)
- No support for authentication/authorization testing (uses local unauthenticated requests)

## Future Enhancements

Potential improvements:
- Support for POST/PUT requests with request bodies
- Multiple concurrent databases/endpoints
- More sophisticated knee detection algorithms
- Grafana/Prometheus export
- Comparison mode (baseline vs. current)
- Warmup quality detection (stop early if stable)
