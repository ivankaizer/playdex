using Api.Infrastructure.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests;

public sealed class CorrelationIdMiddlewareTests
{
    [Theory]
    [InlineData("4f1c2a9e6b7d4e0f8a3b5c6d7e8f9a0b")]
    [InlineData("4f1c2a9e-6b7d-4e0f-8a3b-5c6d7e8f9a0b")]
    [InlineData("client-request-1")]
    public async Task WellFormedClientIdIsKept(string supplied)
    {
        var context = await Run(supplied);

        Assert.Equal(supplied, context.TraceIdentifier);
        Assert.Equal(supplied, context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("line\nbreak")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("id_with_underscore")]
    [InlineData("ünïcode")]
    public async Task MalformedClientIdIsReplaced(string supplied)
    {
        var context = await Run(supplied);

        Assert.NotEqual(supplied, context.TraceIdentifier);
        Assert.Matches("^[0-9a-f]{32}$", context.TraceIdentifier);
        Assert.Equal(context.TraceIdentifier, context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task OverlongClientIdIsReplaced()
    {
        var atLimit = new string('a', CorrelationIdMiddleware.MaxLength);
        Assert.Equal(atLimit, (await Run(atLimit)).TraceIdentifier);

        var tooLong = new string('a', CorrelationIdMiddleware.MaxLength + 1);
        Assert.Matches("^[0-9a-f]{32}$", (await Run(tooLong)).TraceIdentifier);
    }

    [Fact]
    public async Task MissingHeaderGetsAGeneratedId()
    {
        var context = await Run(null);

        Assert.Matches("^[0-9a-f]{32}$", context.TraceIdentifier);
    }

    [Fact]
    public async Task NextMiddlewareSeesTheChosenId()
    {
        string? seen = null;
        var middleware = new CorrelationIdMiddleware(context =>
        {
            seen = context.TraceIdentifier;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc-123";

        await middleware.InvokeAsync(context, NullLogger<CorrelationIdMiddleware>.Instance);

        Assert.Equal("abc-123", seen);
    }

    private static async Task<HttpContext> Run(string? supplied)
    {
        var context = new DefaultHttpContext();
        if (supplied is not null) context.Request.Headers[CorrelationIdMiddleware.HeaderName] = supplied;
        await new CorrelationIdMiddleware(_ => Task.CompletedTask)
            .InvokeAsync(context, NullLogger<CorrelationIdMiddleware>.Instance);
        return context;
    }
}
