using DanielRodrigo.PruebaNET.Application.Stories;
using DanielRodrigo.PruebaNET.TestSupport;

namespace DanielRodrigo.PruebaNET.UnitTests.Stories;

/// <summary>
/// Scripted <see cref="IHackerNewsGateway"/>. Either endpoint can be held behind a gate until the test
/// releases it, so a load can be observed while it is in progress.
/// </summary>
internal sealed class FakeHackerNewsGateway : IHackerNewsGateway
{
    private readonly Lock _gate = new();
    private readonly Dictionary<long, Story> _stories = [];
    private readonly HashSet<long> _failingStoryIds = [];
    private readonly CountSignal _storyRequests = new();

    private long[] _ids = [];
    private bool _idListFails;
    private TaskCompletionSource? _idListHold;
    private TaskCompletionSource? _storyHold;
    private int _idListRequests;
    private int _inFlightStoryRequests;
    private int _maxInFlightStoryRequests;

    public int IdListRequests => Volatile.Read(ref _idListRequests);

    public int StoryRequests => _storyRequests.Count;

    public int InFlightStoryRequests
    {
        get
        {
            lock (_gate)
            {
                return _inFlightStoryRequests;
            }
        }
    }

    public int MaxInFlightStoryRequests
    {
        get
        {
            lock (_gate)
            {
                return _maxInFlightStoryRequests;
            }
        }
    }

    /// <summary>Publishes the ids in the given order and makes every story resolvable.</summary>
    public FakeHackerNewsGateway WithStories(params Story[] stories)
    {
        foreach (var story in stories)
        {
            _stories[story.Id] = story;
        }

        _ids = [.. stories.Select(story => story.Id)];
        return this;
    }

    /// <summary>Publishes ids without touching the items, so an id without a story resolves to null.</summary>
    public FakeHackerNewsGateway WithIds(params long[] ids)
    {
        _ids = ids;
        return this;
    }

    public FakeHackerNewsGateway FailingStory(long id)
    {
        _failingStoryIds.Add(id);
        return this;
    }

    public void FailIdList() => _idListFails = true;

    public void RecoverIdList() => _idListFails = false;

    public void HoldIdList() => Volatile.Write(ref _idListHold, NewGate());

    public void ReleaseIdList() => Interlocked.Exchange(ref _idListHold, null)?.TrySetResult();

    public void HoldStories() => Volatile.Write(ref _storyHold, NewGate());

    public void ReleaseStories() => Interlocked.Exchange(ref _storyHold, null)?.TrySetResult();

    public void ReleaseAll()
    {
        ReleaseIdList();
        ReleaseStories();
    }

    /// <summary>Completes once at least <paramref name="count"/> story requests have started, gated or not.</summary>
    public Task WaitForStoryRequestsAsync(int count) => _storyRequests.WaitForAsync(count);

    public async Task<IReadOnlyList<long>> GetBestStoryIdsAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _idListRequests);
        await WaitIfHeldAsync(Volatile.Read(ref _idListHold), cancellationToken);

        return _idListFails ? throw new HttpRequestException("The best stories list is unavailable.") : _ids;
    }

    public async Task<Story?> GetStoryAsync(long id, CancellationToken cancellationToken)
    {
        RecordStoryRequest();
        try
        {
            await WaitIfHeldAsync(Volatile.Read(ref _storyHold), cancellationToken);

            return _failingStoryIds.Contains(id)
                ? throw new HttpRequestException($"Story {id} is unavailable.")
                : _stories.GetValueOrDefault(id);
        }
        finally
        {
            lock (_gate)
            {
                _inFlightStoryRequests--;
            }
        }
    }

    private void RecordStoryRequest()
    {
        lock (_gate)
        {
            _inFlightStoryRequests++;
            _maxInFlightStoryRequests = Math.Max(_maxInFlightStoryRequests, _inFlightStoryRequests);
        }

        _storyRequests.Increment();
    }

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitIfHeldAsync(TaskCompletionSource? hold, CancellationToken cancellationToken)
    {
        if (hold is not null)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }
    }
}
