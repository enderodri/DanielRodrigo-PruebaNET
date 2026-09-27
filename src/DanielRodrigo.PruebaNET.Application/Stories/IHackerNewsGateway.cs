namespace DanielRodrigo.PruebaNET.Application.Stories;

public interface IHackerNewsGateway
{
    Task<IReadOnlyList<long>> GetBestStoryIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns <c>null</c> when the id does not resolve to a story that can be served
    /// (unknown id, deleted or dead item, or another item type). Transport failures throw.
    /// </summary>
    Task<Story?> GetStoryAsync(long id, CancellationToken cancellationToken);
}
