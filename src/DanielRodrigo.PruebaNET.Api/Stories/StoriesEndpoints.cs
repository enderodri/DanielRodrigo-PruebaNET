using System.Globalization;
using DanielRodrigo.PruebaNET.Application.Stories;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace DanielRodrigo.PruebaNET.Api.Stories;

internal static class StoriesEndpoints
{
    public static IEndpointRouteBuilder MapStoriesEndpoints(this IEndpointRouteBuilder app)
    {
        var maxCount = app.ServiceProvider.GetRequiredService<IOptions<BestStoriesOptions>>().Value.MaxCount;

        app.MapGet("/api/stories/best", GetBestStoriesAsync)
            .WithName("GetBestStories")
            .WithTags("Stories")
            .WithSummary("Best stories")
            .WithDescription($"Returns the best n Hacker News stories ordered by score, highest first. n is an integer between 1 and {maxCount}.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                var n = (OpenApiParameter)operation.Parameters!.Single(parameter => parameter.Name == "n");
                n.Required = true;
                n.Description = $"Number of stories to return, 1 to {maxCount}.";
                n.Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Minimum = "1",
                    Maximum = maxCount.ToString(CultureInfo.InvariantCulture),
                };
                return Task.CompletedTask;
            });

        return app;
    }

    private static async Task<Results<Ok<StoryResponse[]>, ProblemHttpResult>> GetBestStoriesAsync(
        string? n,
        IBestStoriesQuery query,
        IOptions<BestStoriesOptions> options,
        TimeProvider timeProvider,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var maxCount = options.Value.MaxCount;
        if (!int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1 || count > maxCount)
        {
            return TypedResults.Problem(
                title: "Invalid number of stories",
                detail: $"The query parameter 'n' must be an integer between 1 and {maxCount}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await query.GetBestStoriesAsync(count, cancellationToken);

        var interval = options.Value.RefreshInterval;
        var remaining = result.RefreshedAt + interval - timeProvider.GetUtcNow();
        var maxAge = Math.Clamp((int)remaining.TotalSeconds, 0, (int)interval.TotalSeconds);
        response.Headers.CacheControl = $"public, max-age={maxAge}";

        return TypedResults.Ok(result.Stories.Select(StoryResponse.From).ToArray());
    }
}
