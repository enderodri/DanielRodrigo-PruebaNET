using DanielRodrigo.PruebaNET.Application.Stories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DanielRodrigo.PruebaNET.Infrastructure.Hosting;

/// <summary>The only component that starts refreshes: upstream load never depends on inbound traffic.</summary>
internal sealed class BestStoriesRefreshService(
    IBestStoriesRefresher refresher,
    IOptions<BestStoriesOptions> options,
    TimeProvider timeProvider,
    ILogger<BestStoriesRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                var started = timeProvider.GetTimestamp();
                try
                {
                    await refresher.RefreshAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "Scheduled refresh of the best stories failed");
                }

                await Task.Delay(NextDelay(started), timeProvider, stoppingToken);
            }
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private TimeSpan NextDelay(long started)
    {
        var settings = options.Value;
        if (refresher.LastRefreshedAt is null)
        {
            return settings.RetryInterval;
        }

        var untilNext = settings.RefreshInterval - timeProvider.GetElapsedTime(started);
        return untilNext > settings.RetryInterval ? untilNext : settings.RetryInterval;
    }
}
