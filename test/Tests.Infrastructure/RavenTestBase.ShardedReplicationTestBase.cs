using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nito.Disposables;
using Raven.Client.Documents;
using Raven.Client.ServerWide.Sharding;
using Raven.Server;
using Raven.Server.Utils;
using Sparrow.Server;
using Sparrow.Threading;
using Tests.Infrastructure;
using Xunit;
using static FastTests.RavenTestBase.ReplicationManager;

namespace FastTests;

public partial class RavenTestBase
{
    public class ShardedReplicationTestBase
    {
        internal readonly RavenTestBase _parent;

        public ShardedReplicationTestBase(RavenTestBase parent)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        }

        public async Task EnsureReplicatingAsync(IDocumentStore src, IDocumentStore dst)
        {
            var sharding = await _parent.Sharding.GetShardingConfigurationAsync(src);
            foreach (var shardNumber in sharding.Shards.Keys)
            {
                var database = ShardHelper.ToShardName(src.Database, shardNumber);
                var id = $"marker/{Guid.NewGuid()}${_parent.Sharding.GetRandomIdForShard(sharding, shardNumber)}";

                using (var s = src.OpenSession(database))
                {
                    s.Store(new { }, id);
                    s.SaveChanges();
                }

                Assert.NotNull(await _parent.Replication.WaitForDocumentToReplicateAsync<object>(dst, id, 15 * 1000));
            }
        }

        public async Task EnsureReplicatingAsyncForShardedDestination(IDocumentStore src, IDocumentStore dst)
        {
            var sharding = await _parent.Sharding.GetShardingConfigurationAsync(dst);
            foreach (var shardNumber in sharding.Shards.Keys)
            {
                var id = $"marker/{Guid.NewGuid()}${_parent.Sharding.GetRandomIdForShard(sharding, shardNumber)}";

                using (var s = src.OpenSession())
                {
                    s.Store(new { }, id);
                    s.SaveChanges();
                }

                Assert.NotNull(await _parent.Replication.WaitForDocumentToReplicateAsync<object>(dst, id, 30 * 1000));
            }
        }

        public class ShardedReplicationManager : IReplicationManager
        {
            public readonly Dictionary<int, ReplicationManager> ShardReplications;
            public readonly string DatabaseName;
            private readonly ShardingConfiguration _config;

            private ShardedReplicationManager(Dictionary<int, ReplicationManager> shardReplications, string databaseName, ShardingConfiguration config)
            {
                ShardReplications = shardReplications;
                DatabaseName = databaseName;
                _config = config;
            }

            public IAsyncDisposable BreakAsync() => new ComposableAsyncDisposable(ShardReplications.Values.Select(v => v.BreakAsync()));

            public IAsyncDisposable BreakAsync(params string[] docIds) => new ComposableAsyncDisposable(GetManagers(docIds).Select(v => v.BreakAsync()));

            public Task MendAsync() => WhenAll(static v => v.MendAsync());

            public Task MendAsync(params string[] docIds) => Task.WhenAll(GetManagers(docIds).Select(v => v.MendAsync()));

            public Task EnsureNoReplicationLoopAsync() => WhenAll(static v => v.EnsureNoReplicationLoopAsync());

            public Task ReplicateOnceAsync(string docId) => GetShardFor(docId).ReplicateOnceAsync(docId);

            private ReplicationManager GetShardFor(string docId)
            {
                int shardNumber;
                using (var allocator = new ByteStringContext(SharedMultipleUseFlag.None))
                    shardNumber = ShardHelper.GetShardNumberFor(_config, allocator, docId);

                return ShardReplications[shardNumber];
            }

            private IEnumerable<ReplicationManager> GetManagers(string[] docIds)
            {
                HashSet<ReplicationManager> managers = new();

                foreach (string docId in docIds)
                {
                    managers.Add(GetShardFor(docId));
                }

                return managers;
            }

            private Task WhenAll(Func<ReplicationManager, Task> action) => Task.WhenAll(ShardReplications.Values.Select(action));

            public void Dispose()
            {
                foreach (var manager in ShardReplications.Values)
                {
                    manager.Dispose();
                }
            }

            internal static async ValueTask<ShardedReplicationManager> GetShardedReplicationManager(ShardingConfiguration configuration, List<RavenServer> servers,
                string databaseName, ReplicationOptions options)
            {
                Dictionary<int, ReplicationManager> shardReplications = new();
                foreach (var shardNumber in configuration.Shards.Keys)
                {
                    shardReplications.Add(shardNumber, await GetReplicationManagerAsync(servers, ShardHelper.ToShardName(databaseName, shardNumber), options));

                    Assert.True(shardReplications.ContainsKey(shardNumber), $"Couldn't find document database of shard {shardNumber} in any of the servers.");
                }

                return new ShardedReplicationManager(shardReplications, databaseName, configuration);
            }
        }
    }
}
