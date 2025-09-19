using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Raven.Server.Utils;

/// <summary>
/// A breakpoint allowing a synchronous <see cref="Wait"/> and asynchronous configuration using <see cref="BreakAsync"/> and <see cref="ContinueAsync"/>.
/// </summary>
public sealed class AsyncBreakpoint
{
    private readonly string _name;
    private int _count;
    private readonly object _locker = new();

    private TaskCompletionSource _break;
    private int _waiting;
    private int _continuing;

    private TaskCompletionSource _continue;

    public AsyncBreakpoint(string name, int count)
    {
        _name = name;
        _count = count;
    }

    /// <summary>
    /// When hit with the next call of the <see cref="Wait"/>, it will pause its execution synchronously.
    /// The returned task completes when all the waiters wait.
    /// </summary>
    public Task BreakAsync()
    {
        lock (_locker)
        {
            Debug.Assert(_break == null, $"{nameof(BreakAsync)} was already called. Ensure that you call it only once.");
            Debug.Assert(_continue == null);

            _break = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _continue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return _break.Task;
    }

    /// <summary>
    /// Continues the execution of the thread that waits with <see cref="Wait"/> after previously being stopped with <see cref="BreakAsync"/>.
    /// </summary>
    /// <param name="continueCountDown">The number of <see cref="Wait"/> to execute before making this method completed.</param>
    public Task ContinueAsync()
    {
        lock (_locker)
        {
            Debug.Assert(_continue != null, $"The {nameof(ContinueAsync)} can be requested only after {nameof(BreakAsync)} was requested.");

            // Capture the task and then pulse the waiting.
            var task = _continue.Task;

            Monitor.PulseAll(_locker);

            return task;
        }
    }

    public async Task ContinueThenBreakAsync()
    {
        await ContinueAsync();
        await BreakAsync();
    }

    public void Wait(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        // TODO: cancellation

        lock (_locker)
        {
            // Consider waiting only if a break was scheduled.
            if (_break != null)
            {
                _waiting++;

                if (_waiting == _count)
                {
                    // All waiters gathered, set the break and clear the waiters.
                    _break.TrySetResult();
                    _break = null;
                    _waiting = 0;
                }

                Monitor.Wait(_locker);

                // The wait is over, we're continuing.
                Debug.Assert(_continue != null, $"{nameof(ContinueAsync)} should have been called before.");

                _continuing++;
                if (_continuing == _count)
                {
                    // All threads are moving on
                    _continue.TrySetResult();
                    _continue = null;
                    _continuing = 0;
                }
            }
        }
    }

    public override string ToString() => $"{nameof(AsyncBreakpoint)}: {_name}";

    public void SetCount(int replicationLoaderDependentHandlerCount)
    {
        lock (_locker)
        {
            _count = _count;
        }
    }
}
