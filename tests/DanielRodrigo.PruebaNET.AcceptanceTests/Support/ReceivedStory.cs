namespace DanielRodrigo.PruebaNET.AcceptanceTests.Support;

/// <summary>The part of a story in the response that the scenarios talk about.</summary>
public sealed record ReceivedStory(string Title, string? Uri, int Score);
