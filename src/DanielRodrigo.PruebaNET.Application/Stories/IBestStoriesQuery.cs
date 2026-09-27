namespace DanielRodrigo.PruebaNET.Application.Stories;

public interface IBestStoriesQuery
{
    /// <summary>
    /// Returns at most <paramref name="count"/> stories ordered by score, highest first.
    /// Served from memory; before the first snapshot exists the call waits for the load in progress.
    /// </summary>
    /// <exception cref="BestStoriesUnavailableException">No snapshot can be served yet.</exception>
    ValueTask<BestStoriesResult> GetBestStoriesAsync(int count, CancellationToken cancellationToken);
}

public sealed record BestStoriesResult(IReadOnlyList<Story> Stories, DateTimeOffset RefreshedAt);
