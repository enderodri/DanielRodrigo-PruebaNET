using DanielRodrigo.PruebaNET.Application.Stories;
using DanielRodrigo.PruebaNET.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Moq;
using static DanielRodrigo.PruebaNET.UnitTests.Stories.SampleStories;

namespace DanielRodrigo.PruebaNET.UnitTests.Stories;

[TestFixture]
public sealed class BestStoriesLoaderTests
{
    private static readonly DateTimeOffset EarlierRefresh = new(2026, 9, 25, 11, 55, 0, TimeSpan.Zero);

    private FakeHackerNewsGateway _gateway = null!;

    [SetUp]
    public void SetUp() => _gateway = new FakeHackerNewsGateway();

    [TearDown]
    public void TearDown() => _gateway.ReleaseAll();

    [Test]
    public async Task OrdersStoriesByScoreThenById()
    {
        _gateway.WithStories(Story(30, score: 5), Story(10, score: 9), Story(20, score: 9), Story(40, score: 1));

        var stories = await Loader().LoadAsync(previous: null, CancellationToken.None);

        Assert.That(stories.Select(story => story.Id), Is.EqualTo(new long[] { 10, 20, 30, 40 }));
    }

    [Test]
    public async Task SkipsIdsThatDoNotResolveToAStory()
    {
        _gateway.WithStories(Story(1, score: 10), Story(2, score: 5)).WithIds(1, 99, 2);

        var stories = await Loader().LoadAsync(previous: null, CancellationToken.None);

        Assert.That(stories.Select(story => story.Id), Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    public async Task FetchesADuplicatedIdOnce()
    {
        _gateway.WithStories(Story(1, score: 10), Story(2, score: 5)).WithIds(1, 2, 1);

        var stories = await Loader().LoadAsync(previous: null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stories.Select(story => story.Id), Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(_gateway.StoryRequests, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task KeepsThePreviousEntryWhenAStoryRequestFails()
    {
        var staleStory = Story(2, score: 50);
        var previous = new BestStoriesSnapshot([staleStory], EarlierRefresh);
        _gateway.WithStories(Story(1, score: 10), Story(2, score: 60)).FailingStory(2);

        var stories = await Loader(options => options.FailureTolerance = 0.5).LoadAsync(previous, CancellationToken.None);

        Assert.That(stories, Is.EqualTo(new[] { staleStory, Story(1, score: 10) }));
    }

    [Test]
    public async Task DropsAFailedIdWithoutAPreviousEntry()
    {
        _gateway.WithStories(Story(1, score: 10), Story(2, score: 60)).FailingStory(2);

        var stories = await Loader(options => options.FailureTolerance = 0.5).LoadAsync(previous: null, CancellationToken.None);

        Assert.That(stories, Is.EqualTo(new[] { Story(1, score: 10) }));
    }

    [Test]
    public void DiscardsTheRefreshWhenTooManyRequestsFail()
    {
        _gateway.WithStories(TenStories()).FailingStory(3).FailingStory(7);
        var loader = Loader(options => options.FailureTolerance = 0.05);

        var failure = Assert.ThrowsAsync<BestStoriesUnavailableException>(
            () => loader.LoadAsync(previous: null, CancellationToken.None));

        Assert.That(failure?.Message, Does.Contain("2 of 10"));
    }

    [TestCase(0)]
    [TestCase(2)]
    public async Task PublishesWhenFailuresStayWithinTolerance(int failures)
    {
        _gateway.WithStories(TenStories());
        for (var id = 1; id <= failures; id++)
        {
            _gateway.FailingStory(id);
        }

        var stories = await Loader(options => options.FailureTolerance = 0.2).LoadAsync(previous: null, CancellationToken.None);

        Assert.That(stories, Has.Count.EqualTo(10 - failures));
    }

    [Test]
    public void ThrowsWhenTheIdListCannotBeFetched()
    {
        var gateway = new Mock<IHackerNewsGateway>();
        gateway.Setup(g => g.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Hacker News is down."));
        var loader = Loader(gateway: gateway.Object);

        Assert.ThrowsAsync<HttpRequestException>(() => loader.LoadAsync(previous: null, CancellationToken.None));
    }

    [Test]
    public void ThrowsWhenNoStoryLoads()
    {
        _gateway.WithIds(1, 2, 3);
        var loader = Loader();

        Assert.ThrowsAsync<BestStoriesUnavailableException>(() => loader.LoadAsync(previous: null, CancellationToken.None));
    }

    [Test]
    public async Task NeverExceedsFetchConcurrency()
    {
        _gateway.WithStories([.. Enumerable.Range(1, 40).Select(id => Story(id, score: id))]);
        _gateway.HoldStories();

        var load = Loader(options => options.FetchConcurrency = 4).LoadAsync(previous: null, CancellationToken.None);
        await _gateway.WaitForStoryRequestsAsync(4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_gateway.InFlightStoryRequests, Is.EqualTo(4));
            Assert.That(load.IsCompleted, Is.False);
        }

        _gateway.ReleaseStories();
        var stories = await load.OrTimeout();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stories, Has.Count.EqualTo(40));
            Assert.That(_gateway.MaxInFlightStoryRequests, Is.LessThanOrEqualTo(4));
        }
    }

    [Test]
    public async Task PropagatesCancellation()
    {
        _gateway.WithStories(TenStories());
        _gateway.HoldStories();
        using var cancellation = new CancellationTokenSource();

        var load = Loader().LoadAsync(previous: null, cancellation.Token);
        await _gateway.WaitForStoryRequestsAsync(1);
        await cancellation.CancelAsync();

        Assert.CatchAsync<OperationCanceledException>(() => load.OrTimeout());
    }

    [Test]
    public async Task LogsASummaryLine()
    {
        var previous = new BestStoriesSnapshot([Story(3, score: 1)], EarlierRefresh);
        _gateway.WithStories(Story(1, score: 10), Story(2, score: 5), Story(3, score: 7)).WithIds(1, 2, 3, 4).FailingStory(3);
        var logger = new FakeLogger<BestStoriesLoader>();

        await Loader(options => options.FailureTolerance = 0.5, logger).LoadAsync(previous, CancellationToken.None);

        var summaries = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Information).ToList();
        Assert.That(summaries, Has.Count.EqualTo(1));
        Assert.That(summaries[0].Message, Does.Contain("Loaded 3 of 4").And.Contain("1 failed, 1 kept from the previous snapshot"));
    }

    private BestStoriesLoader Loader(
        Action<BestStoriesOptions>? configure = null,
        ILogger<BestStoriesLoader>? logger = null,
        IHackerNewsGateway? gateway = null)
    {
        var options = new BestStoriesOptions();
        configure?.Invoke(options);
        return new BestStoriesLoader(gateway ?? _gateway, Options.Create(options), logger ?? NullLogger<BestStoriesLoader>.Instance);
    }

    private static Story[] TenStories() => [.. Enumerable.Range(1, 10).Select(id => Story(id, score: 100 - id))];
}
