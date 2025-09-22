using System;
using System.Linq;
using System.Threading.Tasks;

namespace Tests.Infrastructure
{
    public interface IReplicationManager : IDisposable
    {
        IAsyncDisposable BreakAsync();

        /// <summary>
        /// Breaks the replication but ONLY for the shards of <paramref name="docIds"/>.
        /// For non-sharded databases works as <see cref="BreakAsync()"/>.
        /// Useful for <see cref="RavenDatabaseMode.Sharded"/>
        /// </summary>
        /// <param name="docIds">The shards to be blocked.</param>
        /// <returns>A break scope that will block the execution until the specific replication hits the break.</returns>
        IAsyncDisposable BreakAsync(params string[] docIds);

        Task MendAsync();
        
        /// <summary>
        /// A counterpart of <see cref="BreakAsync(string[])"/>.
        /// It mends the replications broke by the other one.
        /// Useful for <see cref="RavenDatabaseMode.Sharded"/>
        /// </summary>
        Task MendAsync(params string[] docIds);
        
        Task ReplicateOnceAsync(string docId);
        Task EnsureNoReplicationLoopAsync();
    }
    
    public static class ReplicationManagerExtensions
    {
        /// <summary>
        /// Applies <see cref="IReplicationManager.BreakAsync()"/> but allowing some operation to happen before it's awaited.
        /// </summary>
        /// <param name="manager">The manager to break on.</param>
        /// <param name="managers">Additional managers to break on.</param>
        /// <returns>The scope of the operation.</returns>
        /// <remarks>
        /// Use for nicely looking code with good nesting showing where and what is awaited.
        /// </remarks>
        public static IAsyncDisposable BreakAsync(this IReplicationManager manager, params IReplicationManager[] managers)
        {
            return new ComposableAsyncDisposable(managers.Concat([manager]).Select(m => m.BreakAsync()));
        }
    }
}
