using System.Net;
using DanielRodrigo.PruebaNET.TestSupport;

namespace DanielRodrigo.PruebaNET.IntegrationTests;

/// <summary>What happens to the snapshot once Hacker News stops answering after a successful start.</summary>
[TestFixture]
public sealed class StaleSnapshotTests : ApiFixture
{
    [Test]
    public async Task KeepsServingTheLastSnapshotWhenHackerNewsGoesDown()
    {
        HackerNews.SetBestStories(ThreeStories());
        var lastGoodBody = await WarmUpAsync(3);

        HackerNews.FailAllRequests(HttpStatusCode.ServiceUnavailable);
        await RunScheduledRefreshAsync(BestStories.RefreshInterval);
        using var afterFailedRefresh = await GetBestStoriesAsync(3);

        Clock.Advance(BestStories.RefreshInterval * 3);
        using var longAfter = await GetBestStoriesAsync(3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterFailedRefresh.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await afterFailedRefresh.Content.ReadAsStringAsync(), Is.EqualTo(lastGoodBody));
            Assert.That(longAfter.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await longAfter.Content.ReadAsStringAsync(), Is.EqualTo(lastGoodBody));
            Assert.That(MaxAge(longAfter), Is.EqualTo(TimeSpan.Zero), "a snapshot past its refresh time must not be cached");
        }
    }

    [Test]
    public async Task ReportsDegradedWhenTheSnapshotIsStale()
    {
        HackerNews.SetBestStories(ThreeStories());
        await WarmUpAsync(3);

        HackerNews.FailAllRequests(HttpStatusCode.ServiceUnavailable);
        Clock.Advance((BestStories.RefreshInterval * 3) + TimeSpan.FromSeconds(1));
        using var response = await Client.GetAsync("/health/ready");
        var report = await ReadJsonAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(report["status"]?.GetValue<string>(), Is.EqualTo("Degraded"));
            Assert.That(report["checks"]?["best-stories"]?["status"]?.GetValue<string>(), Is.EqualTo("Degraded"));
        }
    }

    [Test]
    public async Task ServesNewDataAfterTheRefreshInterval()
    {
        HackerNews.SetBestStories(
            (1, HackerNewsItems.Story(1, "Steady", score: 100)),
            (2, HackerNewsItems.Story(2, "Rising", score: 50)));
        var before = await GetTopStoriesAsync(2);
        Assert.That(before.Select(story => story.Title), Is.EqualTo(new[] { "Steady", "Rising" }));

        HackerNews.SetItem(2, HackerNewsItems.Story(2, "Rising", score: 500));
        await RunScheduledRefreshAsync(BestStories.RefreshInterval);

        using var response = await GetBestStoriesAsync(2);
        var after = await ReadStoriesAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.Select(story => (story.Title, story.Score)), Is.EqualTo(new[] { ("Rising", 500), ("Steady", 100) }));
            Assert.That(MaxAge(response), Is.EqualTo(BestStories.RefreshInterval), "the new snapshot is fresh again");
        }
    }

    [Test]
    public async Task DiscardsARefreshWhenStoryRequestsFail()
    {
        HackerNews.SetBestStories(ThreeStories());
        var lastGoodBody = await WarmUpAsync(10);

        HackerNews.SetBestStoryIds(1, 2);
        HackerNews.FailItemRequests(HttpStatusCode.ServiceUnavailable);
        await RunScheduledRefreshAsync(BestStories.RefreshInterval);
        using var afterDiscard = await GetBestStoriesAsync(10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(HackerNews.RequestsForItem(1), Is.GreaterThan(1), "the refresh did ask for the listed stories");
            Assert.That(afterDiscard.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await afterDiscard.Content.ReadAsStringAsync(), Is.EqualTo(lastGoodBody));
        }

        HackerNews.Recover();
        await RunScheduledRefreshAsync(BestStories.RefreshInterval);

        Assert.That(await GetTopStoriesAsync(10), Has.Length.EqualTo(2), "the shorter list is served once item requests succeed");
    }
}
