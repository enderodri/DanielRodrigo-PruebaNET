using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DanielRodrigo.PruebaNET.Application.Stories;
using DanielRodrigo.PruebaNET.Infrastructure.HackerNews;

namespace DanielRodrigo.PruebaNET.UnitTests.HackerNews;

/// <summary>
/// Exercises the typed client against a stub handler: URL building, JSON mapping and the rules for
/// items that are not served. The resilience pipeline is out of scope here.
/// </summary>
[TestFixture]
public sealed class HackerNewsClientTests
{
    private const long SpecSampleId = 21233041;

    private const string SpecSampleJson = """
        {
          "by": "ismaildonmez",
          "descendants": 572,
          "id": 21233041,
          "kids": [21233229, 21233291, 21233281, 21233350],
          "score": 1716,
          "time": 1570887781,
          "title": "A uBlock Origin update was rejected from the Chrome Web Store",
          "type": "story",
          "url": "https://github.com/uBlockOrigin/uBlock-issues/issues/745"
        }
        """;

    private static readonly Uri BaseAddress = new("https://hacker-news.firebaseio.com/v0/");

    private StubHackerNewsHandler _handler = null!;
    private HttpClient _httpClient = null!;
    private HackerNewsClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHackerNewsHandler();
        _httpClient = new HttpClient(_handler) { BaseAddress = BaseAddress };
        _client = new HackerNewsClient(_httpClient);
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient.Dispose();
        _handler.Dispose();
    }

    [Test]
    public async Task ParsesTheStoryFromTheTestBrief()
    {
        var story = await GetStoryAsync(SpecSampleId, SpecSampleJson);

        Assert.That(story, Is.EqualTo(new Story(
            SpecSampleId,
            "A uBlock Origin update was rejected from the Chrome Web Store",
            "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
            "ismaildonmez",
            new DateTimeOffset(2019, 10, 12, 13, 43, 1, TimeSpan.Zero),
            Score: 1716,
            CommentCount: 572)));
    }

    [Test]
    public async Task ParsesTheIdList()
    {
        _handler.RespondTo("beststories.json", "[21233041, 21233229, 8863]");

        var ids = await _client.GetBestStoryIdsAsync(CancellationToken.None);

        Assert.That(ids, Is.EqualTo(new long[] { 21233041, 21233229, 8863 }));
    }

    [Test]
    public async Task ReturnsNullForAnUnknownId()
    {
        var story = await GetStoryAsync(1, "null");

        Assert.That(story, Is.Null);
    }

    [Test]
    public async Task ReturnsNullForADeletedItem()
    {
        var story = await GetStoryAsync(1, """{"id": 1, "deleted": true, "type": "story", "time": 1570887781}""");

        Assert.That(story, Is.Null);
    }

    [Test]
    public async Task ReturnsNullForADeadItem()
    {
        var story = await GetStoryAsync(1, StoryJson(1, customise: item => item["dead"] = true));

        Assert.That(story, Is.Null);
    }

    [TestCase("job")]
    [TestCase("comment")]
    [TestCase("poll")]
    [TestCase("pollopt")]
    public async Task ReturnsNullForOtherItemTypes(string type)
    {
        var story = await GetStoryAsync(1, StoryJson(1, type: type));

        Assert.That(story, Is.Null);
    }

    [Test]
    public async Task ReturnsNullWhenTheStoryHasNoTitle()
    {
        var story = await GetStoryAsync(1, """{"id": 1, "type": "story", "by": "someone", "score": 3, "time": 1570887781}""");

        Assert.That(story, Is.Null);
    }

    [Test]
    public async Task ReturnsNullUrlWhenTheStoryHasNoLink()
    {
        var story = await GetStoryAsync(1, StoryJson(1, url: null));

        Assert.That(story?.Url, Is.Null);
    }

    [Test]
    public async Task ReturnsNullUrlWhenTheUrlIsEmpty()
    {
        var story = await GetStoryAsync(1, StoryJson(1, url: ""));

        Assert.That(story?.Url, Is.Null);
    }

    [Test]
    public async Task DefaultsCommentCountToZeroWhenDescendantsIsMissing()
    {
        var story = await GetStoryAsync(1, StoryJson(1, descendants: null));

        Assert.That(story?.CommentCount, Is.Zero);
    }

    [Test]
    public async Task DecodesHtmlEntitiesInTitles()
    {
        var story = await GetStoryAsync(1, StoryJson(1, title: "AT&amp;T &quot;rocks&quot;"));

        Assert.That(story?.Title, Is.EqualTo("AT&T \"rocks\""));
    }

    [Test]
    public async Task ReturnsAnEmptyListWhenTheIdListIsNull()
    {
        _handler.RespondTo("beststories.json", "null");

        var ids = await _client.GetBestStoryIdsAsync(CancellationToken.None);

        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void ThrowsOnAServerError()
    {
        _handler.RespondTo($"item/{SpecSampleId}.json", HttpStatusCode.InternalServerError);

        Assert.ThrowsAsync<HttpRequestException>(() => _client.GetStoryAsync(SpecSampleId, CancellationToken.None));
    }

    [Test]
    public async Task RequestsTheExpectedPaths()
    {
        _handler.RespondTo("beststories.json", "[]");
        _handler.RespondTo($"item/{SpecSampleId}.json", SpecSampleJson);

        await _client.GetBestStoryIdsAsync(CancellationToken.None);
        await _client.GetStoryAsync(SpecSampleId, CancellationToken.None);

        Assert.That(_handler.Requests, Is.EqualTo(new[]
        {
            new Uri("https://hacker-news.firebaseio.com/v0/beststories.json"),
            new Uri("https://hacker-news.firebaseio.com/v0/item/21233041.json"),
        }));
    }

    private Task<Story?> GetStoryAsync(long id, string json)
    {
        _handler.RespondTo($"item/{id}.json", json);
        return _client.GetStoryAsync(id, CancellationToken.None);
    }

    private static string StoryJson(
        long id,
        string type = "story",
        string title = "A story",
        string? url = "https://example.com/story",
        int? descendants = 12,
        Action<JsonObject>? customise = null)
    {
        var item = new JsonObject
        {
            ["id"] = id,
            ["type"] = type,
            ["title"] = title,
            ["by"] = "someone",
            ["score"] = 42,
            ["time"] = 1570887781,
        };

        if (url is not null)
        {
            item["url"] = url;
        }

        if (descendants is not null)
        {
            item["descendants"] = descendants;
        }

        customise?.Invoke(item);
        return item.ToJsonString();
    }

    private sealed class StubHackerNewsHandler : HttpMessageHandler
    {
        private readonly Dictionary<Uri, Func<HttpResponseMessage>> _responses = [];

        public List<Uri> Requests { get; } = [];

        public void RespondTo(string relativePath, string json) =>
            _responses[new Uri(BaseAddress, relativePath)] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

        public void RespondTo(string relativePath, HttpStatusCode status) =>
            _responses[new Uri(BaseAddress, relativePath)] = () => new HttpResponseMessage(status);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);

            return Task.FromResult(_responses.TryGetValue(uri, out var respond)
                ? respond()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
