namespace DanielRodrigo.PruebaNET.Application.Stories;

public sealed class BestStoriesUnavailableException : Exception
{
    public BestStoriesUnavailableException(string message)
        : base(message)
    {
    }

    public BestStoriesUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
