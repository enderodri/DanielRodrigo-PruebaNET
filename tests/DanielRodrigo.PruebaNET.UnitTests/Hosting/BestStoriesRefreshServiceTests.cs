using DanielRodrigo.PruebaNET.Application.Stories;
using DanielRodrigo.PruebaNET.Infrastructure.Hosting;
using DanielRodrigo.PruebaNET.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace DanielRodrigo.PruebaNET.UnitTests.Hosting;

[TestFixture]
public sealed class BestStoriesRefreshServiceTests
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private FakeBestStoriesRefresher _refresher = null!;
    private TimerAwareFakeTimeProvider _clock = null!;
    private FakeLogger<BestStoriesRefreshService> _logger = null!;
    private BestStoriesRefreshService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _refresher = new FakeBestStoriesRefresher();
        _clock = new TimerAwareFakeTimeProvider(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
        _logger = new FakeLogger<BestStoriesRefreshService>();

        var options = Options.Create(new BestStoriesOptions { RetryInterval = RetryInterval, RefreshInterval = RefreshInterval });
        _service = new BestStoriesRefreshService(_refresher, options, _clock, _logger);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _service.StopAsync(CancellationToken.None).OrTimeout();
        _service.Dispose();
    }

    [Test]
    public async Task RefreshesImmediatelyOnStart()
    {
        await _service.StartAsync(CancellationToken.None);

        await _refresher.WaitForCallsAsync(1);

        Assert.That(_refresher.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task WaitsRetryIntervalWhileThereIsNoSnapshot()
    {
        await StartAndArmAsync(RetryInterval);

        _clock.Advance(RetryInterval - TimeSpan.FromSeconds(1));
        Assert.That(_refresher.Calls, Is.EqualTo(1));

        _clock.Advance(TimeSpan.FromSeconds(1));
        await _refresher.WaitForCallsAsync(2);

        Assert.That(_refresher.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task WaitsRefreshIntervalOnceASnapshotExists()
    {
        _refresher.LastRefreshedAt = _clock.GetUtcNow();
        await StartAndArmAsync(RefreshInterval);

        _clock.Advance(RetryInterval);
        Assert.That(_refresher.Calls, Is.EqualTo(1));

        _clock.Advance(RefreshInterval - RetryInterval);
        await _refresher.WaitForCallsAsync(2);

        Assert.That(_refresher.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task StartsRefreshesOnAFixedCadenceWhateverTheLoadTakes()
    {
        var loadDuration = TimeSpan.FromSeconds(10);
        _refresher.LastRefreshedAt = _clock.GetUtcNow();
        _refresher.WhileRefreshing = () => _clock.Advance(loadDuration);
        await StartAndArmAsync(RefreshInterval - loadDuration);

        _clock.Advance(RefreshInterval - loadDuration - TimeSpan.FromSeconds(1));
        Assert.That(_refresher.Calls, Is.EqualTo(1));

        _clock.Advance(TimeSpan.FromSeconds(1));
        await _refresher.WaitForCallsAsync(2);

        Assert.That(_refresher.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task LeavesAtLeastRetryIntervalAfterALoadLongerThanTheInterval()
    {
        _refresher.LastRefreshedAt = _clock.GetUtcNow();
        _refresher.WhileRefreshing = () => _clock.Advance(RefreshInterval + TimeSpan.FromMinutes(1));
        await StartAndArmAsync(RetryInterval);

        _clock.Advance(RetryInterval - TimeSpan.FromSeconds(1));
        Assert.That(_refresher.Calls, Is.EqualTo(1));

        _clock.Advance(TimeSpan.FromSeconds(1));
        await _refresher.WaitForCallsAsync(2);

        Assert.That(_refresher.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task KeepsGoingAfterAFailedRefresh()
    {
        _refresher.FailsRefreshes = true;
        await StartAndArmAsync(RetryInterval);

        _refresher.FailsRefreshes = false;
        _clock.Advance(RetryInterval);
        await _refresher.WaitForCallsAsync(2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_refresher.Calls, Is.EqualTo(2));
            Assert.That(_logger.Collector.GetSnapshot().Select(record => record.Level), Is.EqualTo(new[] { LogLevel.Error }));
        }
    }

    [Test]
    public async Task StopsWhenTheHostStops()
    {
        await StartAndArmAsync(RetryInterval);

        await _service.StopAsync(CancellationToken.None).OrTimeout();
        _clock.Advance(RefreshInterval + RetryInterval);

        Assert.That(_refresher.Calls, Is.EqualTo(1));
    }

    private async Task StartAndArmAsync(TimeSpan armedDelay)
    {
        await _service.StartAsync(CancellationToken.None);
        await _refresher.WaitForCallsAsync(1);
        await _clock.WaitForTimerAsync(armedDelay);
    }
}
