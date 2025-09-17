using System;
using System.Threading.Tasks;

namespace Tests.Infrastructure
{
    public interface IReplicationManager : IDisposable
    {
        public Task Break();
        public Task Mend();
        public Task ReplicateOnce(string docId);
        public Task EnsureNoReplicationLoopAsync();
    }
}
