using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DanielRodrigo.PruebaNET.Api.Stories;
using DanielRodrigo.PruebaNET.Application.Stories;
using DanielRodrigo.PruebaNET.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DanielRodrigo.PruebaNET.IntegrationTests;

/// <summary>
/// Hosts the API in memory for exactly one test. The host starts, and the background refresh with it,
/// on the first use of <see cref="Client"/>, so the fake Hacker News is configured before any request.
/// </summary>
public abstract class ApiFixture
{
    private BestStoriesApiFactory? _factory;
    private HttpClient? _client;

    protected FakeHackerNewsHandler HackerNews => Factory.HackerNews;

    protected TimerAwareFakeTimeProvider Clock => Factory.Clock;

    protected HttpClient Client => _client ??= Factory.CreateClient();

    /// <summary>The options the running host was configured with, read once the host is up.</summary>
    protected BestStoriesOptions BestStories => Factory.Services.GetRequiredService<IOptions<BestStoriesOptions>>().Value;

    private BestStoriesApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("The API factory is only available while a test runs.");

    [SetUp]
    public void HostTheApi() => _factory = new BestStoriesApiFactory();

    [TearDown]
    public async Task StopTheApi()
    {
        _client?.Dispose();
        _client = null;

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
            _factory = null;
        }
    }

    /// <summary>
    /// Runs the next scheduled refresh to completion: waits for the loop to arm its wake-up, moves the clock,
    /// and joins the refresh so that the new snapshot (or the kept previous one) is published on return.
    /// </summary>
    protected async Task RunScheduledRefreshAsync(TimeSpan armedDelay)
    {
        var listRequestsBefore = HackerNews.BestStoriesRequests;
        HackerNews.HoldBestStories();

        await Clock.WaitForTimerAsync(armedDelay);
        Clock.Advance(armedDelay);
        await HackerNews.WaitForBestStoriesRequestsAsync(listRequestsBefore + 1);

        var refresh = Factory.Services.GetRequiredService<IBestStoriesRefresher>().RefreshAsync(CancellationToken.None);
        HackerNews.ReleaseBestStories();
        await refresh.OrTimeout();
    }

    /// <summary>Three stories listed by Hacker News in an order that is not their score order.</summary>
    protected static (long Id, string Json)[] ThreeStories() =>
    [
        (1, HackerNewsItems.Story(1, "Second best", score: 200)),
        (2, HackerNewsItems.Story(2, "Least voted", score: 50)),
        (3, HackerNewsItems.Story(3, "Top story", score: 900)),
    ];

    protected Task<HttpResponseMessage> GetBestStoriesAsync(int n) =>
        GetBestStoriesAsync(n.ToString(CultureInfo.InvariantCulture));

    protected Task<HttpResponseMessage> GetBestStoriesAsync(string n) =>
        Client.GetAsync($"/api/stories/best?n={n}");

    protected async Task<HttpStatusCode> GetBestStoriesStatusAsync(int n)
    {
        using var response = await GetBestStoriesAsync(n);
        return response.StatusCode;
    }

    /// <summary>Requests the stories and returns the body; a 200 here proves that a snapshot has been loaded.</summary>
    protected async Task<string> WarmUpAsync(int n)
    {
        using var response = await GetBestStoriesAsync(n);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the first snapshot should have been served");
        return await response.Content.ReadAsStringAsync();
    }

    protected async Task<StoryResponse[]> GetTopStoriesAsync(int n)
    {
        using var response = await GetBestStoriesAsync(n);
        return await ReadStoriesAsync(response);
    }

    protected static async Task<StoryResponse[]> ReadStoriesAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<StoryResponse[]>(body, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"Expected a JSON array of stories but got: {body}");
    }

    protected static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(body)
            ?? throw new InvalidOperationException($"Expected a JSON document but got: {body}");
    }

    protected static TimeSpan? MaxAge(HttpResponseMessage response) => response.Headers.CacheControl?.MaxAge;
}
