namespace GameDraw.Automation.Windows.Execution;

internal sealed class PauseGate
{
    private readonly object _lock = new();
    private TaskCompletionSource<bool> _resumeSignal = CompletedSignal();
    private bool _paused;

    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _paused;
            }
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_paused)
            {
                return;
            }

            _paused = true;
            _resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource<bool> signal;
        lock (_lock)
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            signal = _resumeSignal;
            _resumeSignal = CompletedSignal();
        }

        signal.TrySetResult(true);
    }

    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task waitTask;
            lock (_lock)
            {
                if (!_paused)
                {
                    return;
                }

                waitTask = _resumeSignal.Task;
            }

            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TaskCompletionSource<bool> CompletedSignal()
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult(true);
        return signal;
    }
}
