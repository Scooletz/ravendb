using System;
using System.Linq;
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

    public static class ReplicationManagerExtensions
    {
        /// <summary>
        /// Applies <see cref="IReplicationManager.BreakAsync"/> but allowing some operation to happen before it's awaited.
        /// </summary>
        /// <returns>The scope of the operation.</returns>
        /// <remarks>
        /// Use for nicely looking code with good nesting showing where and what is awaited.
        /// </remarks>
        public static IAsyncDisposable BreakThenAwaitAfter(this IReplicationManager manager) => new TaskScope(manager.BreakAsync());

        /// <summary>
        /// Applies <see cref="IReplicationManager.BreakAsync"/> but allowing some operation to happen before it's awaited.
        /// </summary>
        /// <param name="manager">The manager to break on.</param>
        /// <param name="managers">Additional managers to break on.</param>
        /// <returns>The scope of the operation.</returns>
        /// <remarks>
        /// Use for nicely looking code with good nesting showing where and what is awaited.
        /// </remarks>
        public static IAsyncDisposable BreakThenAwaitAfter(this IReplicationManager manager, params IReplicationManager[] managers) => new TaskScope(Task.WhenAll(managers.Concat([manager]).Select(m => m.BreakAsync())));

        private sealed class TaskScope(Task task) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => new(task);
        }
    }
}
