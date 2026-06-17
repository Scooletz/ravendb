using System;
using System.Net.Http;
using System.Threading.Tasks;
using FastTests;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.CdcSink;
using Raven.Client.Documents.Operations.CdcSink.Schema;
using Raven.Client.Documents.Operations.CdcSink.Test;
using Raven.Client.Documents.Operations.ETL.SQL;
using Raven.Client.Exceptions.Sharding;
using Raven.Client.Http;
using Sparrow.Json;
using Tests.Infrastructure;
using Xunit;

namespace SlowTests.Sharding.CdcSink;

/// <summary>
/// Pins the routing wiring for the CDC sink admin endpoints on sharded databases. CDC sinks
/// are not supported on sharded databases as of yet — the goal here is that every endpoint
/// returns a typed <see cref="NotSupportedInShardingException"/> (with a clear message)
/// rather than the generic 500 the routing layer produces when no <c>[RavenShardedAction]</c>
/// is registered (see <c>RouteInformation.TryGetHandler</c>). Surfaced during PR review of
/// RavenDB-26387.
/// </summary>
public class ShardedCdcSinkHandlerTests : RavenTestBase
{
    public ShardedCdcSinkHandlerTests(ITestOutputHelper output) : base(output)
    {
    }

    [RavenFact(RavenTestCategory.Sinks | RavenTestCategory.Sharding)]
    public async Task PostScriptTest_OnShardedDatabase_RejectsWithNotSupportedInSharding()
    {
        using (var store = Sharding.GetDocumentStore())
        {
            // Request contents are intentionally minimal: the sharded handler throws at
            // processor entry before the body is read, so the typed exception must surface
            // regardless of payload validity.
            var request = new TestCdcSinkMappingRequest
            {
                Connection = new SqlConnectionString { FactoryName = "Npgsql", ConnectionString = "Host=ignored" }
            };

            var e = await Assert.ThrowsAsync<NotSupportedInShardingException>(
                () => store.Maintenance.SendAsync(new TestCdcSinkMappingOperation(request)));

            Assert.Contains("CDC Sinks", e.Message);
            Assert.Contains("not supported in sharding", e.Message);
        }
    }

    [RavenFact(RavenTestCategory.Sinks | RavenTestCategory.Sharding)]
    public async Task PostSchema_OnShardedDatabase_RejectsWithNotSupportedInSharding()
    {
        using (var store = Sharding.GetDocumentStore())
        {
            var connection = new SqlConnectionString { FactoryName = "Npgsql", ConnectionString = "Host=ignored" };

            var e = await Assert.ThrowsAsync<NotSupportedInShardingException>(
                () => store.Maintenance.SendAsync(new GetCdcSinkSchemaOperation(connection)));

            Assert.Contains("CDC Sinks", e.Message);
            Assert.Contains("not supported in sharding", e.Message);
        }
    }

    [RavenFact(RavenTestCategory.Sinks | RavenTestCategory.Sharding)]
    public async Task Performance_OnShardedDatabase_RejectsWithNotSupportedInSharding()
    {
        using (var store = Sharding.GetDocumentStore())
        {
            var e = await Assert.ThrowsAsync<NotSupportedInShardingException>(
                () => store.Maintenance.SendAsync(new GetCdcSinkPerformanceOperation()));

            Assert.Contains("CDC Sinks", e.Message);
            Assert.Contains("not supported in sharding", e.Message);
        }
    }

    [RavenFact(RavenTestCategory.Sinks | RavenTestCategory.Sharding)]
    public async Task PerformanceLive_OnShardedDatabase_RejectsWithNotSupportedInSharding()
    {
        // The non-sharded handler upgrades to a WebSocket inside its method body. On the
        // sharded side, the processor throws *before* AcceptWebSocketAsync is reached, so a
        // plain GET (no WS handshake headers) surfaces the typed exception via the normal
        // RavenCommand error path.
        using (var store = Sharding.GetDocumentStore())
        {
            var e = await Assert.ThrowsAsync<NotSupportedInShardingException>(
                () => store.Maintenance.SendAsync(new GetCdcSinkPerformanceLiveOperation()));

            Assert.Contains("CDC Sinks", e.Message);
            Assert.Contains("not supported in sharding", e.Message);
        }
    }

    [RavenFact(RavenTestCategory.Sinks | RavenTestCategory.Sharding)]
    public async Task AddCdcSink_OnShardedDatabase_RejectsWithNotSupportedInSharding()
    {
        using (var store = Sharding.GetDocumentStore())
        {
            // The sharded add-task processor throws in OnBeforeUpdateConfiguration — before any config
            // validation or the actual add — so a minimal configuration is enough to surface the typed
            // exception (PUT /admin/cdc-sink).
            var configuration = new CdcSinkConfiguration
            {
                Name = "cdc-sink-test",
                ConnectionStringName = "ignored"
            };

            var e = await Assert.ThrowsAsync<NotSupportedInShardingException>(
                () => store.Maintenance.SendAsync(new AddCdcSinkOperation(configuration)));

            Assert.Contains("CDC Sinks", e.Message);
            Assert.Contains("not supported in sharding", e.Message);
        }
    }

    private sealed class GetCdcSinkPerformanceOperation : IMaintenanceOperation
    {
        public RavenCommand GetCommand(Raven.Client.Documents.Conventions.DocumentConventions conventions, JsonOperationContext context)
            => new GetCdcSinkPerformanceCommand("/cdc-sink/performance");

        // Shared shell command — only the path differs between the two GET endpoints; the
        // sharded server's typed-exception behavior is what's being asserted on.
        public sealed class GetCdcSinkPerformanceCommand : RavenCommand
        {
            private readonly string _path;

            public GetCdcSinkPerformanceCommand(string path)
            {
                _path = path ?? throw new ArgumentNullException(nameof(path));
            }

            public override bool IsReadRequest => true;

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}{_path}";
                return new HttpRequestMessage { Method = HttpMethod.Get };
            }
        }
    }

    private sealed class GetCdcSinkPerformanceLiveOperation : IMaintenanceOperation
    {
        public RavenCommand GetCommand(Raven.Client.Documents.Conventions.DocumentConventions conventions, JsonOperationContext context)
            => new GetCdcSinkPerformanceOperation.GetCdcSinkPerformanceCommand("/cdc-sink/performance/live");
    }
}
