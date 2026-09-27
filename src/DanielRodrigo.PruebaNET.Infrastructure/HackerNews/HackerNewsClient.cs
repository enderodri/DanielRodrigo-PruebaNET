using System.Net;
using System.Net.Http.Json;
using DanielRodrigo.PruebaNET.Application.Stories;

namespace DanielRodrigo.PruebaNET.Infrastructure.HackerNews;

internal sealed class HackerNewsClient(HttpClient httpClient) : IHackerNewsGateway
{
    public async Task<IReadOnlyList<long>> GetBestStoryIdsAsync(CancellationToken cancellationToken)
    {
        var ids = await httpClient.GetFromJsonAsync("beststories.json", HackerNewsJsonContext.Default.Int64Array, cancellationToken);
        return ids ?? [];
    }

    public async Task<Story?> GetStoryAsync(long id, CancellationToken cancellationToken)
    {
        // Firebase answers an unknown id with 200 and a null body rather than 404.
        var item = await httpClient.GetFromJsonAsync($"item/{id}.json", HackerNewsJsonContext.Default.HackerNewsItem, cancellationToken);
        return item is null ? null : ToStory(item);
    }

    private static Story? ToStory(HackerNewsItem item)
    {
        if (item.Type != "story" || item.Deleted || item.Dead
            || string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.By))
        {
            return null;
        }

        return new Story(
            item.Id,
            WebUtility.HtmlDecode(item.Title),
            string.IsNullOrWhiteSpace(item.Url) ? null : item.Url,
            item.By,
            DateTimeOffset.FromUnixTimeSeconds(item.Time ?? 0),
            item.Score ?? 0,
            item.Descendants ?? 0);
    }
}
