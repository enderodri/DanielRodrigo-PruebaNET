namespace DanielRodrigo.PruebaNET.Application.Stories;

public interface IBestStoriesRefresher
{
    DateTimeOffset? LastRefreshedAt { get; }

    /// <summary>
    /// Loads a fresh snapshot. Concurrent calls share the load in progress. When the load fails and a
    /// previous snapshot exists it is kept; without one the failure is thrown.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken);
}
