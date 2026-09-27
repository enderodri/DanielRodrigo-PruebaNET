using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DanielRodrigo.PruebaNET.Api.Health;

internal static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    data = entry.Value.Data,
                }),
        };

        return context.Response.WriteAsJsonAsync(payload);
    }
}
