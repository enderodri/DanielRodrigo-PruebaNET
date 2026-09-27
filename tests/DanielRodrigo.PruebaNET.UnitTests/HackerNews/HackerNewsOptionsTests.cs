using DanielRodrigo.PruebaNET.Infrastructure;
using DanielRodrigo.PruebaNET.Infrastructure.HackerNews;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace DanielRodrigo.PruebaNET.UnitTests.HackerNews;

[TestFixture]
public sealed class HackerNewsOptionsTests
{
    [Test]
    public void RejectsDisablingRetriesWithItsOwnMessage()
    {
        using var provider = BuildProvider(("HackerNews:MaxRetryAttempts", "0"));

        var failure = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<HackerNewsOptions>>().Value);

        Assert.That(failure?.Message, Does.Contain("HackerNews:MaxRetryAttempts must be at least 1"));
    }

    [Test]
    public void WidensTheBreakerSamplingForALongAttemptTimeout()
    {
        using var provider = BuildProvider(("HackerNews:AttemptTimeout", "00:00:20"), ("HackerNews:TotalTimeout", "00:01:00"));

        var resilience = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{HackerNewsOptions.HttpClientName}-standard");

        Assert.That(resilience.CircuitBreaker.SamplingDuration, Is.EqualTo(TimeSpan.FromSeconds(40)));
    }

    private static ServiceProvider BuildProvider(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHackerNewsGateway();
        return services.BuildServiceProvider();
    }
}
