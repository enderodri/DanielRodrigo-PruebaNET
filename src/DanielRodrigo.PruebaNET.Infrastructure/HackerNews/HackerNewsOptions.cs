namespace DanielRodrigo.PruebaNET.Infrastructure.HackerNews;

public sealed class HackerNewsOptions
{
    public const string SectionName = "HackerNews";
    public const string HttpClientName = "HackerNews";

    public Uri BaseAddress { get; set; } = new("https://hacker-news.firebaseio.com/v0/");

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public int MaxRetryAttempts { get; set; } = 2;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}
