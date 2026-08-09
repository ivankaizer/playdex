using System.Net;
using Web.Observability;
using Xunit;

namespace Web.Tests;

public sealed class SentryApiTracingTests
{
    [Theory]
    [InlineData("https://api.example.test/api/games/123?year=2026", "/api/games/{id}")]
    [InlineData("/api/games/8ec90fd8-76be-4dd7-ae50-b27b655380b1", "/api/games/{id}")]
    [InlineData("/api/games/abcdef0123456789/sessions#recent", "/api/games/{id}/sessions")]
    [InlineData("/api/Games/By-Title", "/api/games/by-title")]
    public void NormalizePathRemovesQueryAndIdentifierCardinality(string url, string expected)
    {
        Assert.Equal(expected, SentryApiTracing.NormalizePath(url));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, Sentry.SpanStatus.Ok)]
    [InlineData(HttpStatusCode.NotFound, Sentry.SpanStatus.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, Sentry.SpanStatus.InternalError)]
    public void ToSpanStatusMapsRepresentativeHttpStatuses(
        HttpStatusCode statusCode,
        Sentry.SpanStatus expected)
    {
        Assert.Equal(expected, SentryApiTracing.ToSpanStatus(statusCode));
    }
}
