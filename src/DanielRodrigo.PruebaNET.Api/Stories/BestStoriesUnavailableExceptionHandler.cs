using System.Globalization;
using DanielRodrigo.PruebaNET.Application.Stories;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DanielRodrigo.PruebaNET.Api.Stories;

internal sealed class BestStoriesUnavailableExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IOptions<BestStoriesOptions> options) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BestStoriesUnavailableException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter =
            ((int)options.Value.RetryInterval.TotalSeconds).ToString(CultureInfo.InvariantCulture);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Best stories are not available",
                Detail = exception.Message,
            },
        });
    }
}
