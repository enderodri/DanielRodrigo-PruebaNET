using Microsoft.Extensions.Time.Testing;

namespace DanielRodrigo.PruebaNET.TestSupport;

/// <summary>
/// Fake clock that can tell a test when a timer with a given due time has been armed. The refresh loop
/// arms its next wake-up on a pool thread after each refresh, so a test that moved the clock straight
/// after a response could do so before the timer exists, and the timer would then never fire.
/// </summary>
public sealed class TimerAwareFakeTimeProvider(DateTimeOffset startTime) : FakeTimeProvider(startTime)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<TimeSpan, int> _armed = [];
    private readonly Dictionary<TimeSpan, int> _consumed = [];
    private readonly List<(TimeSpan DueTime, TaskCompletionSource Signal)> _waiters = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        lock (_gate)
        {
            _armed[dueTime] = _armed.GetValueOrDefault(dueTime) + 1;
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].DueTime == dueTime && TryConsume(dueTime))
                {
                    _waiters[i].Signal.TrySetResult();
                    _waiters.RemoveAt(i);
                }
            }
        }

        return timer;
    }

    /// <summary>
    /// Completes once a timer with this due time has been armed that no earlier call consumed. The refresh
    /// loop is the only code arming RefreshInterval or RetryInterval; the other timers on this clock use
    /// different due times.
    /// </summary>
    public Task WaitForTimerAsync(TimeSpan dueTime)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (TryConsume(dueTime))
            {
                return Task.CompletedTask;
            }

            _waiters.Add((dueTime, signal));
        }

        return signal.Task.OrTimeout();
    }

    private bool TryConsume(TimeSpan dueTime)
    {
        var consumed = _consumed.GetValueOrDefault(dueTime);
        if (_armed.GetValueOrDefault(dueTime) <= consumed)
        {
            return false;
        }

        _consumed[dueTime] = consumed + 1;
        return true;
    }
}
