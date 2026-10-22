using System.Text;
using System.Text.Json;
using Api.Infrastructure.Http;
using Api.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
}
