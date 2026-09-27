using System.Globalization;
using System.Net;
using DanielRodrigo.PruebaNET.AcceptanceTests.Support;
using Reqnroll;

namespace DanielRodrigo.PruebaNET.AcceptanceTests.StepDefinitions;

[Binding]
public sealed class HackerNewsSteps(ApiSession session)
{
    [Given("Hacker News lists the following best stories:")]
    public void GivenHackerNewsListsTheFollowingBestStories(DataTable stories)
    {
        foreach (var row in stories.Rows)
        {
            session.ListStory(row["title"], int.Parse(row["score"], CultureInfo.InvariantCulture));
        }
    }

    [Given("Hacker News lists the best story {string} which has no link")]
    public void GivenHackerNewsListsTheBestStoryWhichHasNoLink(string title) =>
        session.ListStory(title, score: 100, url: null);

    [Given("Hacker News lists {int} best stories")]
    public void GivenHackerNewsListsBestStories(int count) => session.ListSampleStories(count);

    [Given("Hacker News is slow to answer")]
    public void GivenHackerNewsIsSlowToAnswer() => session.HackerNews.HoldBestStories();

    [Given("Hacker News stops responding")]
    public void GivenHackerNewsStopsResponding() =>
        session.HackerNews.FailAllRequests(HttpStatusCode.ServiceUnavailable);

    [When("Hacker News answers")]
    public void WhenHackerNewsAnswers() => session.HackerNews.ReleaseBestStories();

    [When("{int} refresh intervals go by")]
    public async Task WhenRefreshIntervalsGoBy(int intervals)
    {
        var attemptsSoFar = session.HackerNews.BestStoriesRequests;

        await session.Clock.WaitForTimerAsync(session.RefreshInterval);

        session.Clock.Advance((intervals * session.RefreshInterval) + TimeSpan.FromSeconds(1));

        await session.HackerNews.WaitForBestStoriesRequestsAsync(attemptsSoFar + 1);
    }

    [Then("Hacker News was asked for the list of best stories once")]
    public void ThenHackerNewsWasAskedForTheListOfBestStoriesOnce() =>
        Assert.That(session.HackerNews.BestStoriesRequests, Is.EqualTo(1));

    [Then("Hacker News was not asked again")]
    public void ThenHackerNewsWasNotAskedAgain() =>
        Assert.That(session.HackerNews.TotalRequests, Is.EqualTo(session.UpstreamRequestsAfterLoad));
}
