using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DanielRodrigo.PruebaNET.Application.Stories;

/// <summary>
/// Builds the score-ordered list of best stories. Story requests run with bounded concurrency,
/// and a story that fails to load keeps its entry from the previous snapshot when there is one.
/// </summary>
public sealed class BestStoriesLoader(
    IHackerNewsGateway gateway,
    IOptions<BestStoriesOptions> options,
    ILogger<BestStoriesLoader> logger)
{
    public async Task<IReadOnlyList<Story>> LoadAsync(BestStoriesSnapshot? previous, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var settings = options.Value;

        var ids = (await gateway.GetBestStoryIdsAsync(cancellationToken)).Distinct().ToArray();
        var stories = new Story?[ids.Length];
        var failed = 0;
        var reused = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = settings.FetchConcurrency,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForAsync(0, ids.Length, parallelOptions, async (index, token) =>
        {
            var id = ids[index];
            try
            {
                stories[index] = await gateway.GetStoryAsync(id, token);
            }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                Interlocked.Increment(ref failed);
                if (previous is not null && previous.TryGetStory(id, out var stale))
                {
                    stories[index] = stale;
                    Interlocked.Increment(ref reused);
                }

                logger.LogDebug(exception, "Story {StoryId} could not be loaded", id);
            }
        });

        if (failed > ids.Length * settings.FailureTolerance)
        {
            throw new BestStoriesUnavailableException(
                $"{failed} of {ids.Length} story requests failed, more than the tolerated {settings.FailureTolerance:P0}.");
        }

        var ordered = stories
            .OfType<Story>()
            .OrderByDescending(story => story.Score)
            .ThenBy(story => story.Id)
            .ToArray();

        if (ordered.Length == 0)
        {
            throw new BestStoriesUnavailableException($"None of the {ids.Length} best story ids resolved to a story.");
        }

        logger.LogInformation(
            "Loaded {Loaded} of {Ids} best stories in {Elapsed:N0} ms ({Failed} failed, {Reused} kept from the previous snapshot)",
            ordered.Length,
            ids.Length,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            failed,
            reused);

        return ordered;
    }
}
