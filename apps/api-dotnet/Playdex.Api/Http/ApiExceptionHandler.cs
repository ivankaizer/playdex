using Microsoft.AspNetCore.Diagnostics;

namespace Playdex.Api.Http;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var response = ErrorResponseFactory.FromException(exception);
        var level = response.StatusCode >= 500 ? LogLevel.Error : LogLevel.Warning;
        logger.Log(level, exception, "HTTP request failed with status {StatusCode} for {Method} {Path}",
            response.StatusCode,
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = response.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(response.Envelope, cancellationToken);
        return true;
    }
}
