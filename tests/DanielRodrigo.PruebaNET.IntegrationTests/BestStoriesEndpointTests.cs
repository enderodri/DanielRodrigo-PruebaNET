using System.Net;
using DanielRodrigo.PruebaNET.TestSupport;

namespace DanielRodrigo.PruebaNET.IntegrationTests;

[TestFixture]
public sealed class BestStoriesEndpointTests : ApiFixture
{
    [Test]
    public async Task ReturnsTheBestStoriesOrderedByScore()
    {
        HackerNews.SetBestStories(ThreeStories());

        using var response = await GetBestStoriesAsync(2);
        var stories = await ReadStoriesAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(stories.Select(story => story.Title), Is.EqualTo(new[] { "Top story", "Second best" }));
            Assert.That(stories.Select(story => story.Score), Is.Ordered.Descending);
        }
    }

    [Test]
    public async Task ReturnsExactlyTheJsonFromTheBrief()
    {
        HackerNews.SetBestStories((HackerNewsItems.SpecSampleId, HackerNewsItems.SpecSample));

        using var response = await GetBestStoriesAsync(1);
        var body = await response.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.ToString(), Is.EqualTo("application/json; charset=utf-8"));
            Assert.That(
                body,
                Is.EqualTo("""[{"title":"A uBlock Origin update was rejected from the Chrome Web Store","uri":"https://github.com/uBlockOrigin/uBlock-issues/issues/745","postedBy":"ismaildonmez","time":"2019-10-12T13:43:01+00:00","score":1716,"commentCount":572}]"""));
        }
    }

    [Test]
    public async Task ReturnsNullUriForAStoryWithoutALink()
    {
        HackerNews.SetBestStories((1, HackerNewsItems.Story(1, "Ask HN: Where do you host side projects?", score: 40, url: null)));

        using var response = await GetBestStoriesAsync(1);
        var body = await response.Content.ReadAsStringAsync();
        var stories = await ReadStoriesAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stories, Has.Length.EqualTo(1));
            Assert.That(stories[0].Uri, Is.Null);
            Assert.That(body, Does.Contain("\"uri\":null"), "the property is emitted rather than omitted");
        }
    }

    [Test]
    public async Task ExcludesItemsThatAreNotStories()
    {
        const long unknownId = 6;
        HackerNews
            .SetItem(1, HackerNewsItems.Story(1, "The only real story", score: 10))
            .SetItem(192327, HackerNewsItems.ReadmeJob)
            .SetItem(3, HackerNewsItems.OfType(3, "comment"))
            .SetItem(4, HackerNewsItems.Deleted(4))
            .SetItem(5, HackerNewsItems.Dead(5, "Flagged story", score: 999))
            .SetBestStoryIds(192327, 3, 4, 5, unknownId, 1);

        var stories = await GetTopStoriesAsync(10);

        Assert.That(stories.Select(story => story.Title), Is.EqualTo(new[] { "The only real story" }));
    }

    [Test]
    public async Task ReturnsFewerStoriesWhenHackerNewsListsFewerThanN()
    {
        HackerNews.SetBestStories(ThreeStories());

        using var response = await GetBestStoriesAsync(10);
        var stories = await ReadStoriesAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(stories, Has.Length.EqualTo(3));
        }
    }

    [Test]
    public async Task AddsCacheControlUntilTheNextRefresh()
    {
        HackerNews.SetBestStories(ThreeStories());

        using var justRefreshed = await GetBestStoriesAsync(1);
        Clock.Advance(TimeSpan.FromMinutes(2));
        using var twoMinutesLater = await GetBestStoriesAsync(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(justRefreshed.Headers.CacheControl?.Public, Is.True);
            Assert.That(MaxAge(justRefreshed), Is.EqualTo(TimeSpan.FromSeconds(300)));
            Assert.That(MaxAge(twoMinutesLater), Is.EqualTo(TimeSpan.FromSeconds(180)));
        }
    }

    [TestCase("0")]
    [TestCase("501")]
    [TestCase("abc")]
    [TestCase("-1")]
    [TestCase("")]
    public async Task RejectsAnInvalidN(string n)
    {
        HackerNews.SetBestStories(ThreeStories());
        HackerNews.HoldBestStories();

        using var response = await GetBestStoriesAsync(n);

        await AssertInvalidNumberOfStoriesAsync(response);
    }

    [Test]
    public async Task RejectsAMissingN()
    {
        HackerNews.SetBestStories(ThreeStories());
        HackerNews.HoldBestStories();

        using var response = await Client.GetAsync("/api/stories/best");

        await AssertInvalidNumberOfStoriesAsync(response);
    }

    [Test]
    public async Task FetchesAtMostFetchConcurrencyStoriesAtOnce()
    {
        const int listed = 60;
        HackerNews.SetBestStories(Enumerable.Range(1, listed)
            .Select(id => ((long)id, HackerNewsItems.Story(id, $"Story {id}", score: id))));
        HackerNews.HoldItems();

        var request = GetBestStoriesAsync(listed);
        await HackerNews.WaitForItemRequestsAsync(BestStories.FetchConcurrency);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(HackerNews.InFlightItemRequests, Is.EqualTo(BestStories.FetchConcurrency));
            Assert.That(request.IsCompleted, Is.False);
        }

        HackerNews.ReleaseItems();
        using var response = await request;
        var stories = await ReadStoriesAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stories, Has.Length.EqualTo(listed));
            Assert.That(HackerNews.ItemRequests, Is.EqualTo(listed));
            Assert.That(HackerNews.MaxConcurrentItemRequests, Is.EqualTo(BestStories.FetchConcurrency));
        }
    }

    private static async Task AssertInvalidNumberOfStoriesAsync(HttpResponseMessage response)
    {
        var problem = await ReadJsonAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(problem["title"]?.GetValue<string>(), Is.EqualTo("Invalid number of stories"));
        }
    }
}
