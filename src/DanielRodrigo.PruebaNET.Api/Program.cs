using System.Text.Encodings.Web;
using DanielRodrigo.PruebaNET.Api.Health;
using DanielRodrigo.PruebaNET.Api.Stories;
using DanielRodrigo.PruebaNET.Application;
using DanielRodrigo.PruebaNET.Application.Stories;
using DanielRodrigo.PruebaNET.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<BestStoriesOptions>().BindConfiguration(BestStoriesOptions.SectionName);
builder.Services.AddBestStories();
builder.Services.AddHackerNewsGateway();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
    options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BestStoriesUnavailableExceptionHandler>();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Hacker News best stories";
    document.Info.Description = "The best n Hacker News stories ordered by score, served from a snapshot refreshed in the background.";
    return Task.CompletedTask;
}));
builder.Services.AddHealthChecks().AddCheck<BestStoriesHealthCheck>("best-stories", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapStoriesEndpoints();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "Hacker News best stories";
    options.Agent = new ScalarAgentOptions { Disabled = true };
    options.Mcp = new ScalarMcpOptions { Disabled = true };
    options.ShowDeveloperTools = DeveloperToolsVisibility.Never;
    options.Telemetry = false;
});

app.Run();

public partial class Program;
