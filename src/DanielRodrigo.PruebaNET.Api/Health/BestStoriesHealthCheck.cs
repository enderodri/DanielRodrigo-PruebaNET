using DanielRodrigo.PruebaNET.Application.Stories;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DanielRodrigo.PruebaNET.Api.Health;

/// <summary>Ready once a snapshot exists. A stale snapshot is still served, so staleness only degrades.</summary>
internal sealed class BestStoriesHealthCheck(
    IBestStoriesRefresher refresher,
    IOptions<BestStoriesOptions> options,
    TimeProvider timeProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var refreshedAt = refresher.LastRefreshedAt;
        if (refreshedAt is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("No best stories snapshot has been loaded yet."));
        }

        var age = timeProvider.GetUtcNow() - refreshedAt.Value;
        var staleAfter = options.Value.RefreshInterval * 3;
        var data = new Dictionary<string, object>
        {
            ["refreshedAt"] = refreshedAt.Value,
            ["ageSeconds"] = Math.Round(age.TotalSeconds),
        };

        return Task.FromResult(age > staleAfter
            ? HealthCheckResult.Degraded($"Snapshot taken {age.TotalSeconds:F0} s ago; refreshes have been failing.", data: data)
            : HealthCheckResult.Healthy($"Snapshot taken {age.TotalSeconds:F0} s ago.", data));
    }
}
