namespace DanielRodrigo.PruebaNET.Application.Stories;

public sealed record Story(
    long Id,
    string Title,
    string? Url,
    string PostedBy,
    DateTimeOffset Time,
    int Score,
    int CommentCount);
