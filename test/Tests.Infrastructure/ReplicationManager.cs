using System.Collections.Generic;
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

        public async Task BreakAsync()
        {
            foreach (var (node, replicationInstance) in Instances)
            {
                await replicationInstance.BreakAsync();
            }
        }

        public async Task MendAsync()
        {
            foreach (var (node, replicationInstance) in Instances)
            {
                await replicationInstance.MendAsync();
            }
        }

        public async Task ReplicateOnceAsync(string docId)
        {
            foreach (var (node, replicationInstance) in Instances)
            {
                await replicationInstance.ReplicateOnceAsync(docId);
            }
        }

        public async Task EnsureNoReplicationLoopAsync()
        {
            foreach (var (node, replicationInstance) in Instances)
            {
                await replicationInstance.EnsureNoReplicationLoopAsync();
            }
        }

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

            /// <summary>
            /// How many replication processes are handled by this manager.
            /// </summary>
            public int NumberOfReplications = 1;
        }
    }
}
