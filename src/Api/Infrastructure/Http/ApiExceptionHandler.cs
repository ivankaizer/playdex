using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Http;

public sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled API exception for {Method} {Path}")]
    private partial void LogUnhandledApiException(Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API request failed with {StatusCode} for {Method} {Path}: {Message}")]
    private partial void LogApiRequestFailed(int statusCode, string method, string path, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client closed the connection during {Method} {Path}")]
    private partial void LogClientClosedRequest(string method, string path);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path.ToString();
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Nobody is left to read a response, and nothing failed on the server: not an error, not a 500.
            LogClientClosedRequest(method, path);
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        var (statusCode, detail) = Describe(exception);
        if (statusCode >= StatusCodes.Status500InternalServerError && exception is not AppProblemException)
        {
            LogUnhandledApiException(exception, method, path);
        }
        else
        {
            LogApiRequestFailed(statusCode, method, path, detail);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new { status = statusCode, detail, correlationId = httpContext.TraceIdentifier },
            cancellationToken: cancellationToken);
        return true;
    }

    /// <summary>
    /// The status code and a client-safe message. Request errors keep their own status (413, 415, ...) rather than
    /// all being reported as invalid JSON.
    /// </summary>
    internal static (int StatusCode, string Detail) Describe(Exception exception) => exception switch
    {
        AppProblemException problem => (problem.StatusCode, problem.Message),
        JsonException or BadHttpRequestException { InnerException: JsonException } =>
            (StatusCodes.Status400BadRequest, "Invalid JSON body."),
        BadHttpRequestException badRequest => (badRequest.StatusCode, badRequest.StatusCode switch
        {
            StatusCodes.Status408RequestTimeout => "The request body took too long to arrive.",
            StatusCodes.Status413PayloadTooLarge => "The request body is too large.",
            StatusCodes.Status415UnsupportedMediaType => "Send the request body as JSON (Content-Type: application/json).",
            StatusCodes.Status431RequestHeaderFieldsTooLarge => "The request headers are too large.",
            _ => "Bad request.",
        }),
        _ => (StatusCodes.Status500InternalServerError, "Internal server error."),
    };
}
