using System.Text;
using System.Text.Json;
using Api.Infrastructure.Http;
using Api.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests;

public sealed class ApiExceptionHandlerTests
{
    [Theory]
    [InlineData(StatusCodes.Status413PayloadTooLarge, "The request body is too large.")]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, "Send the request body as JSON (Content-Type: application/json).")]
    [InlineData(StatusCodes.Status400BadRequest, "Bad request.")]
    public async Task BadRequestsKeepTheirStatusCode(int statusCode, string detail)
    {
        var context = CreateContext();

        await Handle(context, new BadHttpRequestException("details stay in the logs", statusCode));

        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal(detail, ReadBody(context).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task MalformedJsonIsABadRequest()
    {
        var context = CreateContext();

        await Handle(context, new BadHttpRequestException("bad", new JsonException("unexpected token")));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("Invalid JSON body.", ReadBody(context).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AppProblemsKeepTheirStatusAndMessage()
    {
        var context = CreateContext();

        await Handle(context, new AppProblemException(StatusCodes.Status502BadGateway, "Notification delivery failed."));

        var body = ReadBody(context);
        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Equal("Notification delivery failed.", body.GetProperty("detail").GetString());
        Assert.Equal("correlation-1", body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task AClientThatHungUpIsNotAServerError()
    {
        using var aborted = new CancellationTokenSource();
        var context = CreateContext();
        context.RequestAborted = aborted.Token;
        await aborted.CancelAsync();

        await Handle(context, new OperationCanceledException(aborted.Token));

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task UnexpectedExceptionsAreInternalErrors()
    {
        var context = CreateContext();

        await Handle(context, new InvalidOperationException("secret detail"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("Internal server error.", ReadBody(context).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ANonJsonBodyIsRejectedWith415()
    {
        var context = CreateContext();
        context.Request.ContentType = "text/plain";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        var exception = await Assert.ThrowsAsync<BadHttpRequestException>(() =>
            context.Request.ReadJsonBodyAsync<object>().AsTask());

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, exception.StatusCode);
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, StatusCodes.Status200OK)]
    [InlineData(HealthStatus.Degraded, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(HealthStatus.Unhealthy, StatusCodes.Status503ServiceUnavailable)]
    public void ReadinessIsHealthyOnly(HealthStatus status, int statusCode) =>
        Assert.Equal(statusCode, HealthEndpoints.Readiness.ResultStatusCodes[status]);

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "correlation-1" };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task Handle(HttpContext context, Exception exception) =>
        Assert.True(await new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None));

    private static JsonElement ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement.Clone();
    }

    [Theory]
    [InlineData(400, "Select a valid timezone.")]
    [InlineData(404, "Session not found.")]
    [InlineData(409, "An extraction is already running.")]
    [InlineData(429, "Hourly AI extraction limit reached.")]
    [InlineData(504, "AI extraction timed out.")]
    public async Task AppProblemsAreLoggedAsWarnings(int status, string message)
    {
        var (context, body, logger) = await HandleAndRecord(new AppProblemException(status, message));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(message, body.GetProperty("detail").GetString());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task MalformedJsonFromTheSerializerIsABadRequest()
    {
        var (context, body, logger) = await HandleAndRecord(new JsonException("'}' is invalid after a value. Line 1."));

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("Invalid JSON body.", body.GetProperty("detail").GetString());
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task UnreadableRequestBodyIsABadRequest()
    {
        var (context, body, _) = await HandleAndRecord(new BadHttpRequestException("Unexpected end of request content."));

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("Bad request.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnexpectedExceptionsAreA500WithoutTheirMessage()
    {
        var failure = new InvalidOperationException("connection string Host=db;Password=secret");

        var (context, body, logger) = await HandleAndRecord(failure);

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
        var (_, body, _) = await HandleAndRecord(AppProblemException.NotFound(), traceIdentifier: "abc-123");

        Assert.Equal("abc-123", body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task HandledProblemsComeOutOfTheEnvelopeAsItsError()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        context.Request.Method = "GET";
        context.Request.Path = "/api/things";
        var output = new MemoryStream();
        context.Response.Body = output;
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);
        var middleware = new ApiEnvelopeMiddleware(async inner =>
            await handler.TryHandleAsync(inner, AppProblemException.Conflict("Already running."), CancellationToken.None));

        await middleware.InvokeAsync(context);

        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal(
            """{"data":null,"error":{"error":"Already running.","correlationId":"trace-1"}}""",
            Encoding.UTF8.GetString(output.ToArray()));
    }

    private static async Task<(HttpContext Context, JsonElement Body, RecordingLogger Logger)> HandleAndRecord(
        Exception exception, string? traceIdentifier = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/things";
        if (traceIdentifier is not null)
        {
            context.TraceIdentifier = traceIdentifier;
        }

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
