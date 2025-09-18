using System;
using System.Threading.Tasks;

namespace Tests.Infrastructure
{
    public interface IReplicationManager : IDisposable
    {
        public Task BreakAsync();
        public Task MendAsync();
        public Task ReplicateOnceAsync(string docId);
        public Task EnsureNoReplicationLoopAsync();
    }
}
