using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DanielRodrigo.PruebaNET.Application.Stories;

/// <summary>
/// Holds the current snapshot and coordinates refreshes. Reads never take the lock: they dereference
/// the current snapshot and slice it. Refreshes are single-flight, run under the refresher's token
/// rather than a request's, and only the scheduled refresher starts them.
/// </summary>
public sealed class BestStoriesCache(
    BestStoriesLoader loader,
    IOptions<BestStoriesOptions> options,
    TimeProvider timeProvider,
    ILogger<BestStoriesCache> logger) : IBestStoriesQuery, IBestStoriesRefresher
{
    private readonly Lock _gate = new();
    private volatile BestStoriesSnapshot? _snapshot;
    private TaskCompletionSource<BestStoriesSnapshot>? _inFlight;

    public DateTimeOffset? LastRefreshedAt => _snapshot?.RefreshedAt;

    public async ValueTask<BestStoriesResult> GetBestStoriesAsync(int count, CancellationToken cancellationToken)
    {
        var snapshot = _snapshot ?? await WaitForFirstSnapshotAsync(cancellationToken);
        return new BestStoriesResult(snapshot.Top(count), snapshot.RefreshedAt);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var refresh = new TaskCompletionSource<BestStoriesSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<BestStoriesSnapshot>? pending;

        lock (_gate)
        {
            pending = _inFlight;
            if (pending is null)
            {
                _inFlight = refresh;
            }
        }

        if (pending is not null)
        {
            await pending.Task.WaitAsync(cancellationToken);
            return;
        }

        try
        {
            refresh.SetResult(await LoadAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            refresh.SetException(exception);
            // Nobody may ever await refresh.Task (no joiner, or a joiner that gave up), so mark the fault observed here.
            _ = refresh.Task.Exception;
            throw;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight = null;
            }
        }
    }

    private async Task<BestStoriesSnapshot> WaitForFirstSnapshotAsync(CancellationToken cancellationToken)
    {
        Task<BestStoriesSnapshot>? pending;
        lock (_gate)
        {
            pending = _inFlight?.Task;
        }

        // The refresh publishes the snapshot before it clears the slot, so a caller that saw neither
        // may have arrived between the two writes: look at the snapshot again before waiting or giving up.
        if (_snapshot is { } published)
        {
            return published;
        }

        if (pending is null)
        {
            throw new BestStoriesUnavailableException("No best stories have been loaded yet.");
        }

        try
        {
            return await pending.WaitAsync(options.Value.ColdStartWait, timeProvider, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new BestStoriesUnavailableException("The best stories are still being loaded.", exception);
        }
    }

    private async Task<BestStoriesSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var previous = _snapshot;
        using var deadline = new CancellationTokenSource(options.Value.RefreshTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            var stories = await loader.LoadAsync(previous, linked.Token);
            var snapshot = new BestStoriesSnapshot(stories, timeProvider.GetUtcNow());
            _snapshot = snapshot;
            return snapshot;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (previous is not null)
            {
                logger.LogWarning(exception, "Refresh failed; keeping the snapshot taken at {RefreshedAt}", previous.RefreshedAt);
                return previous;
            }

            if (exception is BestStoriesUnavailableException)
            {
                throw;
            }

            throw new BestStoriesUnavailableException("The best stories could not be loaded from Hacker News.", exception);
        }
    }
}
