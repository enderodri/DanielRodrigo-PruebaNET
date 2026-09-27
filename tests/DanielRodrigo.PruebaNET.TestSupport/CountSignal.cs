namespace DanielRodrigo.PruebaNET.TestSupport;

/// <summary>A counter a test can await: <see cref="WaitForAsync"/> completes once the count reaches the given value.</summary>
public sealed class CountSignal
{
    private readonly Lock _gate = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];
    private int _count;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public int Increment()
    {
        lock (_gate)
        {
            _count++;
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Count <= _count)
                {
                    _waiters[i].Signal.TrySetResult();
                    _waiters.RemoveAt(i);
                }
            }

            return _count;
        }
    }

    public Task WaitForAsync(int count)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_count >= count)
            {
                return Task.CompletedTask;
            }

            _waiters.Add((count, signal));
        }

        return signal.Task.OrTimeout();
    }
}
