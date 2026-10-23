namespace Api.Infrastructure.Http;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        // A client's id is echoed into logs, Sentry and the response, so only a short, plain token is kept.
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsWellFormed(supplied) ? supplied! : Guid.NewGuid().ToString("n");

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlationId"] = correlationId,
        }))
        {
            await next(context);
        }
    }

    /// <summary>1 to 64 ASCII letters, digits or hyphens: enough for GUIDs and trace ids, nothing to inject.</summary>
    public static bool IsWellFormed(string? value) =>
        value is { Length: > 0 and <= MaxLength } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
