using System.Net;
using DanielRodrigo.PruebaNET.TestSupport;

namespace DanielRodrigo.PruebaNET.IntegrationTests;

/// <summary>
/// The brief's core requirement: inbound traffic never turns into Hacker News traffic. Requests are
/// served from the snapshot, and before the first snapshot exists they wait for the one refresh in progress.
/// </summary>
[TestFixture]
public sealed class UpstreamLoadTests : ApiFixture
{
    private const int ConcurrentRequests = 500;
    private static readonly TimeSpan CollectTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task ServesAWarmSnapshotWithoutCallingHackerNews()
    {
        HackerNews.SetBestStories(ThreeStories());
        var expectedBody = await WarmUpAsync(3);
        var upstreamRequestsAfterWarmUp = HackerNews.TotalRequests;

        var replies = await CollectAsync(StartRequests(ConcurrentRequests, n: 3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(replies.Select(reply => reply.Status), Is.All.EqualTo(HttpStatusCode.OK));
            Assert.That(replies.Select(reply => reply.Body), Is.All.EqualTo(expectedBody));
            Assert.That(HackerNews.TotalRequests, Is.EqualTo(upstreamRequestsAfterWarmUp));
        }
    }

    [Test]
    public async Task LoadsOnceWhenManyRequestsArriveBeforeTheFirstSnapshot()
    {
        HackerNews.SetBestStories(ThreeStories());
        HackerNews.HoldBestStories();

        var requests = StartRequests(ConcurrentRequests, n: 3);
        await HackerNews.WaitForBestStoriesRequestsAsync(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(HackerNews.BestStoriesRequests, Is.EqualTo(1));
            Assert.That(requests.Any(request => request.IsCompleted), Is.False, "nothing can be answered before the list arrives");
        }

        HackerNews.ReleaseBestStories();
        var replies = await CollectAsync(requests);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(replies.Select(reply => reply.Status), Is.All.EqualTo(HttpStatusCode.OK));
            Assert.That(replies.Select(reply => reply.Body).Distinct(), Has.Exactly(1).Items);
            Assert.That(HackerNews.TotalRequests, Is.EqualTo(1 + ThreeStories().Length));
        }
    }

    [Test]
    public async Task AnswersWith503WhenHackerNewsIsDownAtStartup()
    {
        HackerNews.SetBestStories(ThreeStories());
        HackerNews.FailAllRequests(HttpStatusCode.ServiceUnavailable);

        using var firstAnswer = await GetBestStoriesAsync(3);
        var upstreamRequestsAfterFirstRefresh = HackerNews.BestStoriesRequests;
        var problem = await ReadJsonAsync(firstAnswer);

        var replies = await CollectAsync(StartRequests(50, n: 3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstAnswer.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(firstAnswer.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(firstAnswer.Headers.RetryAfter?.Delta, Is.EqualTo(BestStories.RetryInterval));
            Assert.That(problem["title"]?.GetValue<string>(), Is.EqualTo("Best stories are not available"));
            Assert.That(replies.Select(reply => reply.Status), Is.All.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(HackerNews.BestStoriesRequests, Is.EqualTo(upstreamRequestsAfterFirstRefresh), "requests must not trigger refreshes");
            Assert.That(HackerNews.ItemRequests, Is.Zero);
        }
    }

    [Test]
    public async Task RecoversOnTheNextAttemptWhenHackerNewsComesBack()
    {
        HackerNews.SetBestStories(ThreeStories());
        HackerNews.FailAllRequests(HttpStatusCode.ServiceUnavailable);
        Assert.That(await GetBestStoriesStatusAsync(3), Is.EqualTo(HttpStatusCode.ServiceUnavailable));

        HackerNews.Recover();
        await RunScheduledRefreshAsync(BestStories.RetryInterval);

        using var response = await GetBestStoriesAsync(3);
        var stories = await ReadStoriesAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(stories.Select(story => story.Title), Is.EqualTo(new[] { "Top story", "Second best", "Least voted" }));
        }
    }

    private Task<HttpResponseMessage>[] StartRequests(int count, int n) =>
        [.. Enumerable.Range(0, count).Select(_ => GetBestStoriesAsync(n))];

    private static async Task<IReadOnlyList<Reply>> CollectAsync(Task<HttpResponseMessage>[] requests)
    {
        var responses = await Task.WhenAll(requests).OrTimeout(CollectTimeout);
        var replies = new List<Reply>(responses.Length);
        foreach (var response in responses)
        {
            using (response)
            {
                replies.Add(new Reply(response.StatusCode, await response.Content.ReadAsStringAsync()));
            }
        }

        return replies;
    }

    private sealed record Reply(HttpStatusCode Status, string Body);
}
