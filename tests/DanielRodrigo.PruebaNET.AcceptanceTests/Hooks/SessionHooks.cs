using DanielRodrigo.PruebaNET.AcceptanceTests.Support;
using Reqnroll;

namespace DanielRodrigo.PruebaNET.AcceptanceTests.Hooks;

[Binding]
public sealed class SessionHooks(ApiSession session)
{
    [AfterScenario]
    public async Task StopTheApiAsync() => await session.DisposeAsync();
}
