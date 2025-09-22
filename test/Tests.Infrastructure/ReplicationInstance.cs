using System;
using System.Linq;
using System.Threading.Tasks;
using FastTests;
using Raven.Server;
using Raven.Server.Documents;
using Raven.Server.Documents.Replication.Stats;
using Raven.Server.Utils;
using Sparrow.Utils;
using Xunit;

namespace Tests.Infrastructure
{
    public class ReplicationInstance : IReplicationManager
    {
        private readonly DocumentDatabase _database;
        public readonly string DatabaseName;
        private readonly RavenTestBase.ReplicationManager.ReplicationOptions _options;
        private readonly AsyncBreakpoint _breakpoint;

        public ReplicationInstance(DocumentDatabase database, string databaseName, RavenTestBase.ReplicationManager.ReplicationOptions options)
        {
            _database = database;
            DatabaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));
            _options = options;

            _breakpoint = database.ReplicationLoader.EnsureBreakpoint();
        }

        public IAsyncDisposable BreakAsync() => new AsyncDisposableScope(_breakpoint.BreakAsync());

        public IAsyncDisposable BreakAsync(params string[] docIds) => BreakAsync();

        public Task MendAsync()
        {
            _database.Configuration.Replication.MaxItemsCount = null;
            return _breakpoint.ContinueAsync();
        }

        public Task MendAsync(params string[] docIds) => MendAsync();

        public async Task ReplicateOnceAsync(string docId)
        {
            _database.Configuration.Replication.MaxItemsCount = _options.MaxItemsCount;
            
            // Should there be a timeout for it?
            await _breakpoint.ContinueAsync();
            await _breakpoint.BreakAsync();
        }

        public async Task EnsureNoReplicationLoopAsync()
        {
            using (var collector = new LiveReplicationPulsesCollector(_database))
            {
                var etag1 = _database.DocumentsStorage.GenerateNextEtag();

                await Task.Delay(3000);

                var etag2 = _database.DocumentsStorage.GenerateNextEtag();

                Assert.True(etag1 + 1 == etag2, $"Replication loop found :( prev: {etag1}, current {etag2}");

                var groups = collector.Pulses.GetAll().GroupBy(p => p.Direction);
                foreach (var group in groups)
                {
                    var key = group.Key;
                    var count = group.Count();
                    Assert.True(count < 50, $"{key} seems to be excessive ({count})");
                }
            }
        }

        public void Dispose()
        {
            _database.ReplicationLoader.DebugBreakpoint = null;
            if (_options.KeepMaxItemsCountOnDispose == false)
                _database.Configuration.Replication.MaxItemsCount = null;
        }

        internal static async ValueTask<ReplicationInstance> GetReplicationInstanceAsync(RavenServer server, string databaseName, RavenTestBase.ReplicationManager.ReplicationOptions options)
        {
            DevelopmentHelper.ShardingToDo(DevelopmentHelper.TeamMember.Stav, DevelopmentHelper.Severity.Normal, "Make this func private when legacy BreakReplication() is removed");
            ReplicationInstance replication = new(await server.ServerStore.DatabasesLandlord.TryGetOrCreateResourceStore(databaseName), databaseName, options);
            
            // TODO: break on start is not possible atm. Needs reworking.
            // if (options.BreakReplicationOnStart)
            //     await replication.BreakAsync();
            
            return replication;
        }
    }
}
