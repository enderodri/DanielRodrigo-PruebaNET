using DanielRodrigo.PruebaNET.Application.Stories;
using DanielRodrigo.PruebaNET.TestSupport;

namespace DanielRodrigo.PruebaNET.UnitTests.Hosting;

/// <summary>Counts refresh calls and lets a test observe them as they happen, whichever thread the scheduler uses.</summary>
internal sealed class FakeBestStoriesRefresher : IBestStoriesRefresher
{
    private readonly CountSignal _calls = new();

    public int Calls => _calls.Count;

    public DateTimeOffset? LastRefreshedAt { get; set; }

    public bool FailsRefreshes { get; set; }

    /// <summary>Runs inside every refresh, for instance to move the fake clock the way a slow load would.</summary>
    public Action? WhileRefreshing { get; set; }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        _calls.Increment();
        WhileRefreshing?.Invoke();

        return FailsRefreshes
            ? Task.FromException(new BestStoriesUnavailableException("The best stories could not be loaded."))
            : Task.CompletedTask;
    }

    public Task WaitForCallsAsync(int count) => _calls.WaitForAsync(count);
}
