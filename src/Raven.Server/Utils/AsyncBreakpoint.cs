using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Raven.Server.Utils;

/// <summary>
/// A breakpoint allowing a synchronous <see cref="Wait"/> and asynchronous configuration using <see cref="Break"/> and <see cref="Continue"/>.
/// </summary>
public sealed class AsyncBreakpoint
{
    private readonly object _locker = new();

    private TaskCompletionSource _break;
    private TaskCompletionSource _continue;
    private int _continueCountDown;

    private CancellationTokenRegistration? _registration;
    private bool _throwCancellation;

    /// <summary>
    /// When hit with the next call of the <see cref="Wait"/>, it will pause its execution synchronously.
    /// The returned 
    /// </summary>
    public Task Break()
    {
        lock (_locker)
        {
            Debug.Assert(_break == null);
            Debug.Assert(_continue == null);

            _break = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _continue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return _break.Task;
    }

    /// <summary>
    /// Continues the execution of the thread that waits with <see cref="Wait"/> after previously being stopped with <see cref="Break"/>.
    /// </summary>
    /// <param name="continueCountDown">The number of <see cref="Wait"/> to execute before making this method completed.</param>
    public Task Continue(int continueCountDown = 1)
    {
        lock (_locker)
        {
            Debug.Assert(_continue != null, $"The {nameof(Continue)} can be requested only after {nameof(Break)} was requested.");
            
            // Capture the task and then pulse the waiting.
            var task = _continue.Task;

            _continueCountDown = continueCountDown;

            Monitor.Pulse(_locker);

            return task;
        }
    }

    public async Task ContinueThenBreak()
    {
        await Continue();
        await Break();
    }
    
    public void Wait(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        lock (_locker)
        {
            // Consider waiting only if a break was scheduled.
            if (_break != null)
            {
                if (cancellationToken.CanBeCanceled)
                {
                    _registration = cancellationToken.Register(OnCancellation, this);
                }

                // We know that there's a break waiting. We inform the _break first, as this wait won't end now. Then we wait
                _break.TrySetResult();
                _break = null;

                Monitor.Wait(_locker);

                // The wait is over either because of the continue or the cancellation. A check is required.
                if (_throwCancellation)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                // Clean up the registration, we continue
                if (_registration != null)
                {
                    _registration.Value.Dispose();
                    _registration = null;
                }
                // It's not a cancellation, it's a continuation. Let it spin one time before announcing continue.
            }

            if (_continue != null)
            {
                if (_continueCountDown > 0)
                {
                    _continueCountDown--;
                }
                else
                {
                    _continue.TrySetResult();
                    _continue = null;
                }
            }
        }
    }

    private static void OnCancellation(object breakpoint) => ((AsyncBreakpoint)breakpoint).CancelWait();

    private void CancelWait()
    {
        lock (_locker)
        {
            _throwCancellation = true;
            Monitor.Pulse(_locker);
        }
    }

    
}
