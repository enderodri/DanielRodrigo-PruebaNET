using System.Diagnostics.CodeAnalysis;

namespace DanielRodrigo.PruebaNET.Application.Stories;

/// <summary>Immutable, score-ordered view of the best stories taken at one point in time.</summary>
public sealed class BestStoriesSnapshot
{
    private readonly Story[] _stories;
    private readonly Dictionary<long, Story> _byId;

    public BestStoriesSnapshot(IEnumerable<Story> storiesOrderedByScore, DateTimeOffset refreshedAt)
    {
        _stories = storiesOrderedByScore.ToArray();
        _byId = new Dictionary<long, Story>(_stories.Length);
        foreach (var story in _stories)
        {
            _byId.Add(story.Id, story);
        }

        RefreshedAt = refreshedAt;
    }

    public DateTimeOffset RefreshedAt { get; }

    public int Count => _stories.Length;

    public IReadOnlyList<Story> Top(int count) =>
        new ArraySegment<Story>(_stories, 0, Math.Clamp(count, 0, _stories.Length));

    public bool TryGetStory(long id, [MaybeNullWhen(false)] out Story story) => _byId.TryGetValue(id, out story);
}
