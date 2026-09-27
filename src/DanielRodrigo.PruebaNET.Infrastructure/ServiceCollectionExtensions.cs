using System.Net;
using DanielRodrigo.PruebaNET.Application.Stories;
using DanielRodrigo.PruebaNET.Infrastructure.HackerNews;
using DanielRodrigo.PruebaNET.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace DanielRodrigo.PruebaNET.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHackerNewsGateway(this IServiceCollection services)
    {
        services.AddOptions<HackerNewsOptions>()
            .BindConfiguration(HackerNewsOptions.SectionName)
            .Validate(o => o.BaseAddress.IsAbsoluteUri, "HackerNews:BaseAddress must be an absolute URI.")
            .Validate(o => o.AttemptTimeout >= TimeSpan.FromMilliseconds(10) && o.TotalTimeout >= o.AttemptTimeout,
                "HackerNews:AttemptTimeout must be at least 10 ms and not longer than HackerNews:TotalTimeout.")
            .Validate(o => o.MaxRetryAttempts >= 1 && o.RetryDelay >= TimeSpan.Zero,
                "HackerNews:MaxRetryAttempts must be at least 1 and HackerNews:RetryDelay must not be negative.")
            .ValidateOnStart();

        services.AddHttpClient<IHackerNewsGateway, HackerNewsClient>(HackerNewsOptions.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = provider.GetRequiredService<IOptions<HackerNewsOptions>>().Value.BaseAddress;
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,
                EnableMultipleHttp2Connections = true,
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler()
            .Configure((resilience, provider) =>
            {
                var settings = provider.GetRequiredService<IOptions<HackerNewsOptions>>().Value;
                resilience.AttemptTimeout.Timeout = settings.AttemptTimeout;
                resilience.TotalRequestTimeout.Timeout = settings.TotalTimeout;
                resilience.Retry.MaxRetryAttempts = settings.MaxRetryAttempts;
                resilience.Retry.Delay = settings.RetryDelay;

                // The pipeline refuses to start unless the breaker samples at least twice the attempt timeout.
                var minimumSampling = settings.AttemptTimeout * 2;
                if (resilience.CircuitBreaker.SamplingDuration < minimumSampling)
                {
                    resilience.CircuitBreaker.SamplingDuration = minimumSampling;
                }
            });

        services.AddHostedService<BestStoriesRefreshService>();

        return services;
    }
}
