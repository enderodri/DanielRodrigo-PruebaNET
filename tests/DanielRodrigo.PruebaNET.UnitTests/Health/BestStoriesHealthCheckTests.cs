using DanielRodrigo.PruebaNET.Api.Health;
using DanielRodrigo.PruebaNET.Application.Stories;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace DanielRodrigo.PruebaNET.UnitTests.Health;

[TestFixture]
public sealed class BestStoriesHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private Mock<IBestStoriesRefresher> _refresher = null!;
    private BestStoriesHealthCheck _check = null!;

    [SetUp]
    public void SetUp()
    {
        _refresher = new Mock<IBestStoriesRefresher>();
        _check = new BestStoriesHealthCheck(
            _refresher.Object,
            Options.Create(new BestStoriesOptions { RefreshInterval = RefreshInterval }),
            new FakeTimeProvider(Now));
    }

    [Test]
    public async Task IsUnhealthyBeforeTheFirstSnapshot()
    {
        _refresher.SetupGet(refresher => refresher.LastRefreshedAt).Returns((DateTimeOffset?)null);

        var result = await CheckAsync();

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    [Test]
    public async Task IsHealthyWhileTheSnapshotIsRecent()
    {
        var refreshedAt = Now.AddMinutes(-2);
        _refresher.SetupGet(refresher => refresher.LastRefreshedAt).Returns(refreshedAt);

        var result = await CheckAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(result.Data["refreshedAt"], Is.EqualTo(refreshedAt));
            Assert.That(result.Data["ageSeconds"], Is.EqualTo(120d));
        }
    }

    [Test]
    public async Task StaysHealthyUntilThreeIntervalsHavePassed()
    {
        _refresher.SetupGet(refresher => refresher.LastRefreshedAt).Returns(Now - (RefreshInterval * 3));

        var result = await CheckAsync();

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task IsDegradedOnceTheSnapshotIsOlderThanThreeIntervals()
    {
        _refresher.SetupGet(refresher => refresher.LastRefreshedAt).Returns(Now - (RefreshInterval * 3) - TimeSpan.FromSeconds(1));

        var result = await CheckAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
            Assert.That(result.Description, Does.Contain("refreshes have been failing"));
        }
    }

    private Task<HealthCheckResult> CheckAsync() => _check.CheckHealthAsync(new HealthCheckContext());
}
