using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DanielRodrigo.PruebaNET.AcceptanceTests.Support;
using DanielRodrigo.PruebaNET.TestSupport;
using Reqnroll;

namespace DanielRodrigo.PruebaNET.AcceptanceTests.StepDefinitions;

[Binding]
public sealed class ServiceSteps(ApiSession session)
{
    [Given("the best stories have been loaded")]
    public async Task GivenTheBestStoriesHaveBeenLoaded()
    {
        session.ListSampleStories(5);
        session.LoadedStories = await ReceivedStoriesAsync(await session.AskForBestStoriesAsync(5));
        session.UpstreamRequestsAfterLoad = session.HackerNews.TotalRequests;
    }

    [When("a client asks for the best {int} story/stories")]
    public async Task WhenAClientAsksForTheBestStories(int count) => await session.AskForBestStoriesAsync(count);

    [When("{int} clients ask for the best {int} stories at the same time")]
    public async Task WhenClientsAskForTheBestStoriesAtTheSameTime(int clients, int count)
    {
        session.StartTheApi();
        await session.HackerNews.WaitForBestStoriesRequestsAsync(1);

        session.AskForBestStoriesConcurrently(clients, count);
    }

    [Then("it receives, in this order:")]
    public async Task ThenItReceivesInThisOrder(DataTable expected)
    {
        var expectedStories = expected.Rows
            .Select(row => (Title: row["title"], Score: int.Parse(row["score"], CultureInfo.InvariantCulture)))
            .ToArray();

        var received = await ReceivedStoriesAsync(session.LastResponse);

        Assert.That(received.Select(story => (story.Title, story.Score)), Is.EqualTo(expectedStories));
    }

    [Then("it receives {string} without a link")]
    public async Task ThenItReceivesWithoutALink(string title)
    {
        var received = await ReceivedStoriesAsync(session.LastResponse);

        Assert.That(received, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received[0].Title, Is.EqualTo(title));
            Assert.That(received[0].Uri, Is.Null);
        }
    }

    [Then("every client receives the same {int} stories")]
    public async Task ThenEveryClientReceivesTheSameStories(int count)
    {
        var responses = await Task.WhenAll(session.ConcurrentRequests).OrTimeout();
        var received = await Task.WhenAll(responses.Select(ReceivedStoriesAsync));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(received[0], Has.Count.EqualTo(count));
            Assert.That(received, Is.All.EqualTo(received[0]));
        }
    }

    [Then("clients still receive the stories")]
    public async Task ThenClientsStillReceiveTheStories()
    {
        Assert.That(session.LoadedStories, Is.Not.Null, "The scenario must load the stories before Hacker News goes away.");

        var received = await ReceivedStoriesAsync(await session.AskForBestStoriesAsync(session.LoadedStories.Count));

        Assert.That(received, Is.EqualTo(session.LoadedStories));
    }

    [Then("the service reports that its data is stale")]
    public async Task ThenTheServiceReportsThatItsDataIsStale()
    {
        using var response = await session.Client.GetAsync("/health/ready");
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.That(report.GetProperty("status").GetString(), Is.EqualTo("Degraded"), report.ToString());
    }

    [Then("the request is rejected as invalid")]
    public void ThenTheRequestIsRejectedAsInvalid() =>
        Assert.That(session.LastResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

    private static async Task<IReadOnlyList<ReceivedStory>> ReceivedStoriesAsync(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<ReceivedStory[]>()
            ?? throw new InvalidOperationException("The service answered 200 with an empty body.");
    }
}
