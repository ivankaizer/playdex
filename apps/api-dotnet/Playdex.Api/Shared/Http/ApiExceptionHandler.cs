using Microsoft.AspNetCore.Diagnostics;

namespace Playdex.Api.Shared.Http;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken ct)
    {
        var appProblem = exception as AppProblem;
        var statusCode = appProblem?.StatusCode ?? 500;
        var detail = appProblem?.Message ?? "Internal server error.";

        if (appProblem is null)
        {
            logger.LogError(
                exception,
                "Unhandled API exception for {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(
                "API request failed with {StatusCode} for {Method} {Path}: {Message}",
                statusCode,
                httpContext.Request.Method,
                httpContext.Request.Path,
                detail);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new { status = statusCode, detail, correlationId = httpContext.TraceIdentifier },
            cancellationToken: ct);
        return true;
    }
}
