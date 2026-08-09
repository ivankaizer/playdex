using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Http;

public sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled API exception for {Method} {Path}")]
    private partial void LogUnhandledApiException(Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API request failed with {StatusCode} for {Method} {Path}: {Message}")]
    private partial void LogApiRequestFailed(int statusCode, string method, string path, string message);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var appProblem = exception as AppProblemException;
        var invalidJson = exception is System.Text.Json.JsonException or BadHttpRequestException;
        var statusCode = appProblem?.StatusCode ?? (invalidJson ? 400 : 500);
        var detail = appProblem?.Message ?? (invalidJson ? "Invalid JSON body." : "Internal server error.");

        if (appProblem is null && !invalidJson)
        {
            LogUnhandledApiException(exception, httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            LogApiRequestFailed(statusCode, httpContext.Request.Method, httpContext.Request.Path, detail);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new { status = statusCode, detail, correlationId = httpContext.TraceIdentifier },
            cancellationToken: cancellationToken);
        return true;
    }
}
