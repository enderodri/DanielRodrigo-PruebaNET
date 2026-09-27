namespace DanielRodrigo.PruebaNET.TestSupport;

public static class TaskExtensions
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static Task OrTimeout(this Task task, TimeSpan? timeout = null) => task.WaitAsync(timeout ?? DefaultTimeout);

    public static Task<T> OrTimeout<T>(this Task<T> task, TimeSpan? timeout = null) => task.WaitAsync(timeout ?? DefaultTimeout);
}
