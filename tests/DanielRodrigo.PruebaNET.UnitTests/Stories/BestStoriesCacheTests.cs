using DanielRodrigo.PruebaNET.Application.Stories;
using DanielRodrigo.PruebaNET.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static DanielRodrigo.PruebaNET.UnitTests.Stories.SampleStories;

namespace DanielRodrigo.PruebaNET.UnitTests.Stories;

[TestFixture]
public sealed class BestStoriesCacheTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly long[] AllIdsByScore = [1, 2, 3, 4, 5];

    private FakeHackerNewsGateway _gateway = null!;
    private FakeTimeProvider _clock = null!;
    private BestStoriesOptions _options = null!;
    private BestStoriesCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _gateway = new FakeHackerNewsGateway().WithStories(
            Story(1, score: 50), Story(2, score: 40), Story(3, score: 30), Story(4, score: 20), Story(5, score: 10));
        _clock = new FakeTimeProvider(StartTime);
        _options = new BestStoriesOptions();

        var options = Options.Create(_options);
        var loader = new BestStoriesLoader(_gateway, options, NullLogger<BestStoriesLoader>.Instance);
        _cache = new BestStoriesCache(loader, options, _clock, NullLogger<BestStoriesCache>.Instance);
    }

    [TearDown]
    public void TearDown() => _gateway.ReleaseAll();

    [Test]
    public async Task ServesTheSnapshotWithoutTouchingTheGateway()
    {
        await _cache.RefreshAsync(CancellationToken.None);

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => GetBestStories(3)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(result => result.Stories.Count), Is.All.EqualTo(3));
            Assert.That(_gateway.IdListRequests, Is.EqualTo(1));
            Assert.That(_gateway.StoryRequests, Is.EqualTo(5));
        }
    }

    [Test]
    public async Task ReturnsAtMostNStories()
    {
        await _cache.RefreshAsync(CancellationToken.None);

        var result = await GetBestStories(3);

        Assert.That(result.Stories.Select(story => story.Id), Is.EqualTo(AllIdsByScore.Take(3)));
    }

    [Test]
    public async Task ReturnsAllStoriesWhenNIsLarger()
    {
        await _cache.RefreshAsync(CancellationToken.None);

        var result = await GetBestStories(500);

        Assert.That(result.Stories.Select(story => story.Id), Is.EqualTo(AllIdsByScore));
    }

    [Test]
    public async Task ExposesTheRefreshTime()
    {
        _clock.Advance(TimeSpan.FromMinutes(7));

        await _cache.RefreshAsync(CancellationToken.None);
        var result = await GetBestStories(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RefreshedAt, Is.EqualTo(StartTime.AddMinutes(7)));
            Assert.That(_cache.LastRefreshedAt, Is.EqualTo(StartTime.AddMinutes(7)));
        }
    }

    [Test]
    public void ThrowsUnavailableWhenNothingIsLoadedAndNoRefreshIsRunning()
    {
        Assert.ThrowsAsync<BestStoriesUnavailableException>(() => GetBestStories(3));
        Assert.That(_cache.LastRefreshedAt, Is.Null);
    }

    [Test]
    public async Task ColdStartCallersWaitForTheRefreshInProgress()
    {
        _gateway.HoldIdList();
        var refresh = _cache.RefreshAsync(CancellationToken.None);
        var callers = Enumerable.Range(0, 200).Select(_ => GetBestStories(3)).ToArray();

        Assert.That(callers.Select(caller => caller.IsCompleted), Is.All.False);

        _gateway.ReleaseIdList();
        var results = await Task.WhenAll(callers).OrTimeout();
        await refresh.OrTimeout();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(result => result.Stories.Select(story => story.Id)), Is.All.EqualTo(AllIdsByScore.Take(3)));
            Assert.That(_gateway.IdListRequests, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ConcurrentRefreshesShareOneLoad()
    {
        _gateway.HoldIdList();
        var refreshes = Enumerable.Range(0, 5).Select(_ => _cache.RefreshAsync(CancellationToken.None)).ToArray();

        _gateway.ReleaseIdList();
        await Task.WhenAll(refreshes).OrTimeout();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_gateway.IdListRequests, Is.EqualTo(1));
            Assert.That(_gateway.StoryRequests, Is.EqualTo(5));
            Assert.That(_cache.LastRefreshedAt, Is.EqualTo(StartTime));
        }
    }

    [Test]
    public async Task ACancelledWaiterDoesNotCancelTheLoad()
    {
        _gateway.HoldIdList();
        var refresh = _cache.RefreshAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var cancelledCaller = _cache.GetBestStoriesAsync(3, cancellation.Token).AsTask();

        await cancellation.CancelAsync();
        Assert.CatchAsync<OperationCanceledException>(() => cancelledCaller.OrTimeout());

        var patientCaller = GetBestStories(3);
        _gateway.ReleaseIdList();
        var result = await patientCaller.OrTimeout();
        await refresh.OrTimeout();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Stories.Select(story => story.Id), Is.EqualTo(AllIdsByScore.Take(3)));
            Assert.That(_gateway.IdListRequests, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task KeepsThePreviousSnapshotWhenARefreshFails()
    {
        await _cache.RefreshAsync(CancellationToken.None);
        _clock.Advance(_options.RefreshInterval);
        _gateway.FailIdList();

        await _cache.RefreshAsync(CancellationToken.None);
        var result = await GetBestStories(500);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Stories.Select(story => story.Id), Is.EqualTo(AllIdsByScore));
            Assert.That(result.RefreshedAt, Is.EqualTo(StartTime));
            Assert.That(_gateway.IdListRequests, Is.EqualTo(2));
        }
    }

    [Test]
    public void ThrowsUnavailableWhenTheFirstLoadFails()
    {
        _gateway.FailIdList();
        _gateway.HoldIdList();
        var refresh = _cache.RefreshAsync(CancellationToken.None);
        var waiter = GetBestStories(3);

        _gateway.ReleaseIdList();
        var refreshFailure = Assert.ThrowsAsync<BestStoriesUnavailableException>(() => refresh.OrTimeout());
        var waiterFailure = Assert.ThrowsAsync<BestStoriesUnavailableException>(() => waiter.OrTimeout());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(refreshFailure?.InnerException, Is.TypeOf<HttpRequestException>());
            Assert.That(waiterFailure?.InnerException, Is.TypeOf<HttpRequestException>());
            Assert.That(_cache.LastRefreshedAt, Is.Null);
        }
    }

    [Test]
    public async Task TheNextRefreshCanSucceedAfterAFailedFirstLoad()
    {
        _gateway.FailIdList();
        Assert.ThrowsAsync<BestStoriesUnavailableException>(() => _cache.RefreshAsync(CancellationToken.None));

        _gateway.RecoverIdList();
        await _cache.RefreshAsync(CancellationToken.None);
        var result = await GetBestStories(500);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Stories.Select(story => story.Id), Is.EqualTo(AllIdsByScore));
            Assert.That(_cache.LastRefreshedAt, Is.EqualTo(StartTime));
        }
    }

    [Test]
    public async Task ColdStartWaitGivesUpAfterTheConfiguredTime()
    {
        _gateway.HoldIdList();
        var refresh = _cache.RefreshAsync(CancellationToken.None);
        var waiter = GetBestStories(3);

        _clock.Advance(_options.ColdStartWait + TimeSpan.FromSeconds(1));
        var failure = Assert.ThrowsAsync<BestStoriesUnavailableException>(() => waiter.OrTimeout());

        Assert.That(failure?.InnerException, Is.TypeOf<TimeoutException>());

        _gateway.ReleaseIdList();
        await refresh.OrTimeout();
        var result = await GetBestStories(3);

        Assert.That(result.Stories, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task AnOverrunningLoadIsCancelledAtRefreshTimeout()
    {
        _gateway.HoldStories();
        var refresh = _cache.RefreshAsync(CancellationToken.None);
        await _gateway.WaitForStoryRequestsAsync(1);

        _clock.Advance(_options.RefreshTimeout + TimeSpan.FromSeconds(1));
        var failure = Assert.ThrowsAsync<BestStoriesUnavailableException>(() => refresh.OrTimeout());
        _gateway.ReleaseStories();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure?.InnerException, Is.InstanceOf<OperationCanceledException>());
            Assert.That(_cache.LastRefreshedAt, Is.Null);
            Assert.ThrowsAsync<BestStoriesUnavailableException>(() => GetBestStories(3));
        }
    }

    private Task<BestStoriesResult> GetBestStories(int count) => _cache.GetBestStoriesAsync(count, CancellationToken.None).AsTask();
}
