using DanielRodrigo.PruebaNET.Application.Stories;

namespace DanielRodrigo.PruebaNET.UnitTests.Stories;

internal static class SampleStories
{
    public static readonly DateTimeOffset PostedAt = new(2019, 10, 12, 13, 43, 1, TimeSpan.Zero);

    public static Story Story(long id, int score) =>
        new(id, $"Story {id}", $"https://example.com/{id}", "someone", PostedAt, score, CommentCount: 0);
}
