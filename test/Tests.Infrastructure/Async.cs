using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tests.Infrastructure
{
    /// <summary>
    /// A simple disposable scope that awaits the task at its disposal. 
    /// </summary>
    public sealed class AsyncDisposableScope(Task task) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(task);
    }

    /// <summary>
    /// A simple composable <see cref="IAsyncDisposable"/> that iterates over its disposable with no error handling.
    /// </summary>
    public sealed class ComposableAsyncDisposable(IEnumerable<IAsyncDisposable> disposables) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (IAsyncDisposable d in disposables)
            {
                await d.DisposeAsync();
            }
        }
    }
}
