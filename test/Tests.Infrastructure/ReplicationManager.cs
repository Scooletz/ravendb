using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Raven.Server;
using Tests.Infrastructure;

namespace FastTests;

public partial class RavenTestBase
{
    public class ReplicationManager : IReplicationManager
    {
        public readonly string DatabaseName;
        public readonly Dictionary<string, ReplicationInstance> Instances;

        public ReplicationManager(string databaseName, Dictionary<string, ReplicationInstance> instances)
        {
            DatabaseName = databaseName;
            Instances = instances;
        }

        public IAsyncDisposable BreakAsync()
        {
            return new ComposableAsyncDisposable(Instances.Values.Select(i => i.BreakAsync()));
        }

        public IAsyncDisposable BreakAsync(params string[] docIds)
        {
            return new ComposableAsyncDisposable(Instances.Values.Select(i => i.BreakAsync(docIds)));
        }

        public Task MendAsync() => WhenAll(static i => i.MendAsync());

        public Task MendAsync(params string[] docIds) => WhenAll(i => i.MendAsync(docIds));

        public Task ReplicateOnceAsync(string docId) => WhenAll(i => i.ReplicateOnceAsync(docId));

        public Task EnsureNoReplicationLoopAsync() => WhenAll(static i => i.EnsureNoReplicationLoopAsync());

        private Task WhenAll(Func<ReplicationInstance, Task> action) => Task.WhenAll(Instances.Values.Select(action));

        public void Dispose()
        {
            foreach (var instance in Instances.Values)
            {
                instance.Dispose();
            }
        }

        internal static async ValueTask<ReplicationManager> GetReplicationManagerAsync(List<RavenServer> servers, string databaseName, ReplicationOptions options)
        {
            Dictionary<string, ReplicationInstance> instances = new();
            foreach (var server in servers)
            {
                var instance = await ReplicationInstance.GetReplicationInstanceAsync(server, databaseName, options);
                if (instance != null)
                    instances[server.ServerStore.NodeTag] = instance;
            }

            return new ReplicationManager(databaseName, instances);
        }

        public class ReplicationOptions
        {
            public bool BreakReplicationOnStart = true;
            public bool KeepMaxItemsCountOnDispose;
            public int? MaxItemsCount = 1;
        }
    }
}
