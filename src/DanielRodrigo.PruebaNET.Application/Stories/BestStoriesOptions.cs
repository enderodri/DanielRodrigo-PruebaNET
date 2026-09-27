namespace DanielRodrigo.PruebaNET.Application.Stories;

public sealed class BestStoriesOptions
{
    public const string SectionName = "BestStories";

    public int MaxCount { get; set; } = 500;

    /// <summary>Time between two snapshot refreshes. Together with the list size this is the whole upstream load.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Time between attempts while no snapshot has been loaded yet.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan RefreshTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long a request may wait for the first snapshot before it is answered with 503.</summary>
    public TimeSpan ColdStartWait { get; set; } = TimeSpan.FromSeconds(10);

    public int FetchConcurrency { get; set; } = 16;

    /// <summary>Fraction of story requests that may fail before a refresh is discarded and the previous snapshot kept.</summary>
    public double FailureTolerance { get; set; } = 0.05;
}
