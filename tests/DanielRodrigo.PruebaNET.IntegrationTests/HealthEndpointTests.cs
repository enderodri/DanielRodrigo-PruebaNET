using System.Net;
using DanielRodrigo.PruebaNET.TestSupport;

namespace DanielRodrigo.PruebaNET.IntegrationTests;

[TestFixture]
public sealed class HealthEndpointTests : ApiFixture
{
    [Test]
    public async Task LiveIsHealthyBeforeTheFirstSnapshot()
    {
        HackerNews.SetBestStories(ThreeStories());
        HackerNews.HoldBestStories();

        using var response = await Client.GetAsync("/health/live");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
        }
    }

    [Test]
    public async Task ReadyIsUnhealthyBeforeTheFirstSnapshot()
    {
        HackerNews.SetBestStories(ThreeStories());
        HackerNews.HoldBestStories();

        using var response = await Client.GetAsync("/health/ready");
        var report = await ReadJsonAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(report["status"]?.GetValue<string>(), Is.EqualTo("Unhealthy"));
            Assert.That(report["checks"]?["best-stories"]?["status"]?.GetValue<string>(), Is.EqualTo("Unhealthy"));
        }
    }

    [Test]
    public async Task ReadyIsHealthyOnceASnapshotExists()
    {
        HackerNews.SetBestStories(ThreeStories());
        await WarmUpAsync(3);

        using var response = await Client.GetAsync("/health/ready");
        var report = await ReadJsonAsync(response);
        var bestStories = report["checks"]?["best-stories"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(report["status"]?.GetValue<string>(), Is.EqualTo("Healthy"));
            Assert.That(bestStories?["status"]?.GetValue<string>(), Is.EqualTo("Healthy"));
            Assert.That(bestStories?["data"]?["refreshedAt"]?.GetValue<string>(), Is.EqualTo("2026-09-25T12:00:00+00:00"));
        }
    }
}
