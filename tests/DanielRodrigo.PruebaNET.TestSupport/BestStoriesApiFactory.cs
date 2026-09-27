using DanielRodrigo.PruebaNET.Infrastructure.HackerNews;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace DanielRodrigo.PruebaNET.TestSupport;

/// <summary>
/// Hosts the API in memory with a fake Hacker News and a fake clock. Create one per test: the
/// handler counts requests and the clock is advanced explicitly, so instances must not be shared.
/// </summary>
public sealed class BestStoriesApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset StartTime = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    public FakeHackerNewsHandler HackerNews { get; } = new();

    public TimerAwareFakeTimeProvider Clock { get; } = new(StartTime);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient(HackerNewsOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => HackerNews);

            services.PostConfigureAll<HttpStandardResilienceOptions>(resilience =>
            {
                resilience.Retry.Delay = TimeSpan.Zero;
                resilience.CircuitBreaker.MinimumThroughput = int.MaxValue;
            });
        });
    }
}
