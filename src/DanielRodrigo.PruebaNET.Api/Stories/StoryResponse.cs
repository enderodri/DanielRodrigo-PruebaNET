using DanielRodrigo.PruebaNET.Application.Stories;

namespace DanielRodrigo.PruebaNET.Api.Stories;

public sealed record StoryResponse(
    string Title,
    string? Uri,
    string PostedBy,
    DateTimeOffset Time,
    int Score,
    int CommentCount)
{
    public static StoryResponse From(Story story) =>
        new(story.Title, story.Url, story.PostedBy, story.Time, story.Score, story.CommentCount);
}
