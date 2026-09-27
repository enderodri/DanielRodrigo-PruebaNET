using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DanielRodrigo.PruebaNET.TestSupport;

/// <summary>
/// Stands in for hacker-news.firebaseio.com at the HttpMessageHandler level, so the typed client,
/// JSON parsing and resilience pipeline run for real. Counts every request and can hold or fail responses.
/// </summary>
public sealed partial class FakeHackerNewsHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<long, string> _items = new();
    private readonly ConcurrentDictionary<long, int> _requestsPerItem = new();
    private readonly CountSignal _bestStoriesRequests = new();
    private readonly CountSignal _itemRequests = new();

    private long[] _bestStoryIds = [];
    private int _inFlightItemRequests;
    private int _maxConcurrentItemRequests;
    private TaskCompletionSource? _bestStoriesHold;
    private TaskCompletionSource? _itemsHold;
    private Func<HttpRequestMessage, HttpStatusCode?>? _failure;

    public int BestStoriesRequests => _bestStoriesRequests.Count;

    public int ItemRequests => _itemRequests.Count;

    public int TotalRequests => BestStoriesRequests + ItemRequests;

    public int InFlightItemRequests => Volatile.Read(ref _inFlightItemRequests);

    public int MaxConcurrentItemRequests => Volatile.Read(ref _maxConcurrentItemRequests);

    public int RequestsForItem(long id) => _requestsPerItem.GetValueOrDefault(id);

    /// <summary>Publishes the given items as the best stories list, in the given order.</summary>
    public FakeHackerNewsHandler SetBestStories(params IEnumerable<(long Id, string Json)> items)
    {
        var ids = new List<long>();
        foreach (var (id, json) in items)
        {
            ids.Add(id);
            _items[id] = json;
        }

        _bestStoryIds = [.. ids];
        return this;
    }

    public FakeHackerNewsHandler SetBestStoryIds(params long[] ids)
    {
        _bestStoryIds = ids;
        return this;
    }

    public FakeHackerNewsHandler SetItem(long id, string json)
    {
        _items[id] = json;
        return this;
    }

    public FakeHackerNewsHandler RemoveItem(long id)
    {
        _items.TryRemove(id, out _);
        return this;
    }

    /// <summary>Delays the response to the best stories list until <see cref="ReleaseBestStories"/> is called.</summary>
    public void HoldBestStories() => Volatile.Write(ref _bestStoriesHold, NewHold());

    public void ReleaseBestStories() => Interlocked.Exchange(ref _bestStoriesHold, null)?.TrySetResult();

    /// <summary>Delays every item response until <see cref="ReleaseItems"/> is called; requests are still counted as they arrive.</summary>
    public void HoldItems() => Volatile.Write(ref _itemsHold, NewHold());

    public void ReleaseItems() => Interlocked.Exchange(ref _itemsHold, null)?.TrySetResult();

    public Task WaitForBestStoriesRequestsAsync(int count) => _bestStoriesRequests.WaitForAsync(count);

    public Task WaitForItemRequestsAsync(int count) => _itemRequests.WaitForAsync(count);

    public void FailAllRequests(HttpStatusCode status) => _failure = _ => status;

    public void FailItemRequests(HttpStatusCode status) =>
        _failure = request => ItemPath().IsMatch(request.RequestUri!.AbsolutePath) ? status : null;

    public void Recover() => _failure = null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/beststories.json", StringComparison.Ordinal))
        {
            _bestStoriesRequests.Increment();
            await WaitIfHeldAsync(Volatile.Read(ref _bestStoriesHold), cancellationToken);

            return _failure?.Invoke(request) is { } status
                ? new HttpResponseMessage(status)
                : Json(JsonSerializer.Serialize(_bestStoryIds));
        }

        var match = ItemPath().Match(path);
        if (!match.Success)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var id = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        _requestsPerItem.AddOrUpdate(id, 1, (_, count) => count + 1);

        var inFlight = Interlocked.Increment(ref _inFlightItemRequests);
        try
        {
            int observedMax;
            while (inFlight > (observedMax = Volatile.Read(ref _maxConcurrentItemRequests)))
            {
                Interlocked.CompareExchange(ref _maxConcurrentItemRequests, inFlight, observedMax);
            }

            _itemRequests.Increment();
            await WaitIfHeldAsync(Volatile.Read(ref _itemsHold), cancellationToken);

            return _failure?.Invoke(request) is { } status
                ? new HttpResponseMessage(status)
                : Json(_items.TryGetValue(id, out var json) ? json : "null");
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightItemRequests);
        }
    }

    private static TaskCompletionSource NewHold() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitIfHeldAsync(TaskCompletionSource? hold, CancellationToken cancellationToken)
    {
        if (hold is not null)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [GeneratedRegex(@"/item/(\d+)\.json$")]
    private static partial Regex ItemPath();
}
