using System.Net;
using System.Text.Json;
using Api.Infrastructure.Http;
using Api.Modules.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests;

public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public async Task AppriseFailuresReturnActionableGatewayProblem()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethod.Post.Method;
        context.Request.Path = "/api/settings/notifications/test";
        context.Response.Body = new MemoryStream();
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);

        await handler.TryHandleAsync(
            context,
            new AppriseApiException("Apprise notification failed with HTTP 424."),
            CancellationToken.None);

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal((int)HttpStatusCode.BadGateway, context.Response.StatusCode);
        Assert.Equal(
            "Notification delivery failed. Check the Telegram chat ID and try again.",
            body.RootElement.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(400, "Select a valid timezone.")]
    [InlineData(404, "Session not found.")]
    [InlineData(409, "An extraction is already running.")]
    [InlineData(429, "Hourly AI extraction limit reached.")]
    [InlineData(504, "AI extraction timed out.")]
    public async Task AppProblemsKeepTheirStatusAndMessage(int status, string message)
    {
        var (context, body, logger) = await Handle(new AppProblemException(status, message));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(message, body.GetProperty("detail").GetString());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task MalformedJsonIsABadRequest()
    {
        var (context, body, logger) = await Handle(new JsonException("'}' is invalid after a value. Line 1."));

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("Invalid JSON body.", body.GetProperty("detail").GetString());
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task UnreadableRequestBodyIsABadRequest()
    {
        var (context, body, _) = await Handle(new BadHttpRequestException("Unexpected end of request content."));

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("Invalid JSON body.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnexpectedExceptionsAreA500WithoutTheirMessage()
    {
        var failure = new InvalidOperationException("connection string Host=db;Password=secret");

        var (context, body, logger) = await Handle(failure);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("Internal server error.", body.GetProperty("detail").GetString());
        Assert.DoesNotContain("secret", body.GetRawText(), StringComparison.Ordinal);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(failure, entry.Exception);
        Assert.Contains("/api/things", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProblemCarriesTheRequestsCorrelationId()
    {
        var (_, body, _) = await Handle(AppProblemException.NotFound(), traceIdentifier: "abc-123");

        Assert.Equal("abc-123", body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task HandledProblemsComeOutOfTheEnvelopeAsItsError()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/things";
        var output = new MemoryStream();
        context.Response.Body = output;
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);
        var middleware = new ApiEnvelopeMiddleware(async inner =>
            await handler.TryHandleAsync(inner, AppProblemException.Conflict("Already running."), CancellationToken.None));

        await middleware.InvokeAsync(context);

        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("""{"data":null,"error":{"error":"Already running."}}""", System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    private static async Task<(HttpContext Context, JsonElement Body, RecordingLogger Logger)> Handle(
        Exception exception, string? traceIdentifier = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/things";
        if (traceIdentifier is not null) context.TraceIdentifier = traceIdentifier;
        context.Response.Body = new MemoryStream();
        var logger = new RecordingLogger();

        var handled = await new ApiExceptionHandler(logger).TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context, document.RootElement.Clone(), logger);
    }

    private sealed class RecordingLogger : ILogger<ApiExceptionHandler>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
