using System.Globalization;
using DanielRodrigo.PruebaNET.Application.Stories;
using DanielRodrigo.PruebaNET.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DanielRodrigo.PruebaNET.AcceptanceTests.Support;

/// <summary>
/// What one scenario shares between its steps: the API hosted in memory, the fake Hacker News
/// behind it, the fake clock, and what the clients have asked for so far. Reqnroll creates one
/// per scenario through context injection and the hooks dispose it.
/// </summary>
public sealed class ApiSession : IAsyncDisposable
{
    private const long FirstStoryId = 1000;

    private readonly BestStoriesApiFactory _factory = new();
    private readonly List<(long Id, string Json)> _listedStories = [];
    private HttpClient? _client;
    private HttpResponseMessage? _lastResponse;

    public FakeHackerNewsHandler HackerNews => _factory.HackerNews;

    public TimerAwareFakeTimeProvider Clock => _factory.Clock;

    /// <summary>The host starts with the first client, so Hacker News must be set up before a client asks for anything.</summary>
    public HttpClient Client => _client ??= _factory.CreateClient();

    public TimeSpan RefreshInterval =>
        _factory.Services.GetRequiredService<IOptions<BestStoriesOptions>>().Value.RefreshInterval;

    public HttpResponseMessage LastResponse =>
        _lastResponse ?? throw new InvalidOperationException("No client has asked for stories yet.");

    public IReadOnlyList<Task<HttpResponseMessage>> ConcurrentRequests { get; private set; } = [];

    public IReadOnlyList<ReceivedStory>? LoadedStories { get; set; }

    /// <summary>Requests Hacker News had received once the first snapshot was served.</summary>
    public int UpstreamRequestsAfterLoad { get; set; }

    public void StartTheApi() => _ = Client;

    /// <summary>Appends a story to the list Hacker News publishes. Ids are sequential; the order of the list is irrelevant to the service.</summary>
    public void ListStory(string title, int score, string? url = "https://example.com/story")
    {
        var id = FirstStoryId + _listedStories.Count;
        _listedStories.Add((id, HackerNewsItems.Story(id, title, score, url: url)));
        HackerNews.SetBestStories(_listedStories);
    }

    public void ListSampleStories(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            ListStory($"Sample story {i}", score: 1000 - (3 * i));
        }
    }

    public async Task<HttpResponseMessage> AskForBestStoriesAsync(int count)
    {
        _lastResponse = await Client.GetAsync(BestStoriesUrl(count));
        return _lastResponse;
    }

    public void AskForBestStoriesConcurrently(int clients, int count)
    {
        var client = Client;
        ConcurrentRequests = Enumerable.Range(0, clients).Select(_ => client.GetAsync(BestStoriesUrl(count))).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        HackerNews.ReleaseBestStories();
        _client?.Dispose();
        await _factory.DisposeAsync();
    }

    private static string BestStoriesUrl(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"/api/stories/best?n={count}");
}
