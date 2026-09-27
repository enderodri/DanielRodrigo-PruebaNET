using DanielRodrigo.PruebaNET.Application.Stories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DanielRodrigo.PruebaNET.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBestStories(this IServiceCollection services)
    {
        services.AddOptions<BestStoriesOptions>()
            .Validate(o => o.MaxCount >= 1, "BestStories:MaxCount must be at least 1.")
            .Validate(o => o.RefreshInterval > TimeSpan.Zero, "BestStories:RefreshInterval must be positive.")
            .Validate(o => o.RetryInterval > TimeSpan.Zero, "BestStories:RetryInterval must be positive.")
            .Validate(o => o.RefreshTimeout > TimeSpan.Zero, "BestStories:RefreshTimeout must be positive.")
            .Validate(o => o.ColdStartWait > TimeSpan.Zero, "BestStories:ColdStartWait must be positive.")
            .Validate(o => o.FetchConcurrency >= 1, "BestStories:FetchConcurrency must be at least 1.")
            .Validate(o => o.FailureTolerance is >= 0 and <= 1, "BestStories:FailureTolerance must be between 0 and 1.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<BestStoriesLoader>();
        services.AddSingleton<BestStoriesCache>();
        services.AddSingleton<IBestStoriesQuery>(provider => provider.GetRequiredService<BestStoriesCache>());
        services.AddSingleton<IBestStoriesRefresher>(provider => provider.GetRequiredService<BestStoriesCache>());

        return services;
    }
}
