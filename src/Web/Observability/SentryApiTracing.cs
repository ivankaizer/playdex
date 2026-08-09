using System.Net;
using Sentry;

namespace Web.Observability;

public static class SentryApiTracing
{
    public static ITransactionTracer? Start(HttpMethod method, string url)
    {
        if (!SentrySdk.IsEnabled)
        {
            return null;
        }

        var transaction = SentrySdk.StartTransaction(
            $"{method.Method.ToUpperInvariant()} {NormalizePath(url)}",
            "ui.api_request");
        transaction.SetTag("component", "web");
        return transaction;
    }

    public static void AddTraceHeader(HttpRequestMessage request, ITransactionTracer? transaction)
    {
        if (transaction is null)
        {
            return;
        }

        request.Headers.TryAddWithoutValidation(
            "sentry-trace",
            transaction.GetTraceHeader().ToString());
    }

    public static SpanStatus ToSpanStatus(HttpStatusCode statusCode) => (int)statusCode switch
    {
        < 400 => SpanStatus.Ok,
        400 => SpanStatus.FailedPrecondition,
        401 => SpanStatus.Unauthenticated,
        403 => SpanStatus.PermissionDenied,
        404 => SpanStatus.NotFound,
        409 => SpanStatus.AlreadyExists,
        429 => SpanStatus.ResourceExhausted,
        499 => SpanStatus.Cancelled,
        < 500 => SpanStatus.FailedPrecondition,
        501 => SpanStatus.Unimplemented,
        503 => SpanStatus.Unavailable,
        504 => SpanStatus.DeadlineExceeded,
        < 600 => SpanStatus.InternalError,
        _ => SpanStatus.UnknownError,
    };

    public static string NormalizePath(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            ? absolute.AbsolutePath
            : url.Split('?', '#')[0];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => IsIdentifier(segment) ? "{id}" : segment.ToLowerInvariant());
        return "/" + string.Join('/', segments);
    }

    private static bool IsIdentifier(string segment)
    {
        if (long.TryParse(segment, out _) || Guid.TryParse(segment, out _))
        {
            return true;
        }

        return segment.Length >= 16 && segment.All(Uri.IsHexDigit);
    }
}
