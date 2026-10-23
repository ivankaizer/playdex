using System.Text;
using System.Text.Json;
using Api.Infrastructure.Http;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Api.Tests;

public sealed class ApiEnvelopeMiddlewareTests
{
    [Fact]
    public async Task JsonPayloadsAreWrappedInData()
    {
        var (context, body) = await Run(StatusCodes.Status200OK, "application/json; charset=utf-8", """{"id":1}""");

        Assert.Equal(1, body.RootElement.GetProperty("data").GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("error").ValueKind);
        Assert.Equal("application/json", context.Response.ContentType);
    }

    [Theory]
    [InlineData("text/plain", "plain text")]
    [InlineData("text/csv", "id,title\n1,Hades")]
    [InlineData("application/json", "not json")]
    public async Task OtherSuccessfulResponsesPassThroughUnchanged(string contentType, string payload)
    {
        var (context, raw) = await RunRaw(StatusCodes.Status200OK, contentType, payload);

        Assert.Equal(payload, raw);
        Assert.Equal(contentType, context.Response.ContentType);
    }

    [Fact]
    public async Task ErrorsCarryTheCorrelationId()
    {
        var (_, body) = await Run(StatusCodes.Status404NotFound, "application/json", """{"detail":"Game not found."}""");

        var error = body.RootElement.GetProperty("error");
        Assert.Equal("Game not found.", error.GetProperty("error").GetString());
        Assert.Equal("correlation-1", error.GetProperty("correlationId").GetString());
    }

    private static async Task<(HttpContext Context, JsonDocument Body)> Run(int status, string contentType, string payload)
    {
        var (context, raw) = await RunRaw(status, contentType, payload);
        return (context, JsonDocument.Parse(raw));
    }

    private static async Task<(HttpContext Context, string Body)> RunRaw(int status, string contentType, string payload)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "correlation-1" };
        context.Request.Path = "/api/test";
        var output = new MemoryStream();
        context.Response.Body = output;
        var middleware = new ApiEnvelopeMiddleware(async http =>
        {
            http.Response.StatusCode = status;
            http.Response.ContentType = contentType;
            await http.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(payload));
        });

        await middleware.InvokeAsync(context);

        return (context, Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public async Task SuccessfulJsonIsWrappedAsData()
    {
        var response = await Capture(201, "application/json; charset=utf-8", """{"id":7,"names":["a","b"]}""");

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("application/json", response.ContentType);
        Assert.Null(response.ContentLength);
        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal(7, body.RootElement.GetProperty("data").GetProperty("id").GetInt32());
        Assert.Equal(["a", "b"], body.RootElement.GetProperty("data").GetProperty("names").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("error").ValueKind);
    }

    [Theory]
    [InlineData("\"Ok\"", JsonValueKind.String)]
    [InlineData("[1,2]", JsonValueKind.Array)]
    [InlineData("42", JsonValueKind.Number)]
    public async Task NonObjectPayloadsAreWrappedToo(string payload, JsonValueKind kind)
    {
        var response = await Capture(200, "application/json", payload);

        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal(kind, body.RootElement.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task EmptySuccessfulBodyBecomesNullData()
    {
        var response = await Capture(200, null, "");

        Assert.Equal("""{"data":null,"error":null}""", response.Body);
    }

    [Fact]
    public async Task NoContentStaysEmpty()
    {
        var response = await Capture(204, "application/json", "");

        Assert.Equal(204, response.StatusCode);
        Assert.Equal("", response.Body);
        Assert.Null(response.ContentType);
    }

    [Theory]
    [InlineData("image/webp")]
    [InlineData("image/png")]
    [InlineData("application/octet-stream")]
    public async Task FilesPassThroughUnwrapped(string contentType)
    {
        var response = await Capture(200, contentType, "\u0001binary\u0002");

        Assert.Equal("\u0001binary\u0002", response.Body);
        Assert.Equal(contentType, response.ContentType);
        Assert.Equal(Encoding.UTF8.GetByteCount("\u0001binary\u0002"), response.ContentLength);
    }

    [Fact]
    public async Task ProblemDetailBecomesTheEnvelopeError()
    {
        var response = await Capture(409, "application/problem+json", """{"status":409,"detail":"Already running."}""");

        Assert.Equal(409, response.StatusCode);
        Assert.Equal("""{"data":null,"error":{"error":"Already running.","correlationId":"trace-1"}}""", response.Body);
    }

    [Fact]
    public async Task AlreadyEnvelopedErrorIsNotWrappedTwice()
    {
        var response = await Capture(400, "application/json", """{"data":null,"error":{"error":"Bad input."}}""");

        Assert.Equal("""{"data":null,"error":{"error":"Bad input.","correlationId":"trace-1"}}""", response.Body);
    }

    [Theory]
    [InlineData(400, "Bad request.")]
    [InlineData(401, "Unauthorized.")]
    [InlineData(403, "Forbidden.")]
    [InlineData(404, "Route not found.")]
    [InlineData(409, "Request failed.")]
    [InlineData(429, "Request failed.")]
    [InlineData(500, "Internal server error.")]
    [InlineData(503, "Internal server error.")]
    public async Task ErrorsWithoutAUsableMessageGetOneFromTheStatus(int status, string expected)
    {
        foreach (var (contentType, body) in new[]
                 {
                     ((string?)null, ""),
                     ("text/plain", "upstream exploded"),
                     ("application/json", """{"detail":"   "}"""),
                     ("application/json", """{"title":"No detail here"}"""),
                 })
        {
            var response = await Capture(status, contentType, body);

            using var envelope = JsonDocument.Parse(response.Body);
            Assert.Equal(expected, envelope.RootElement.GetProperty("error").GetProperty("error").GetString());
            Assert.Equal(JsonValueKind.Null, envelope.RootElement.GetProperty("data").ValueKind);
        }
    }

    [Fact]
    public async Task RequestsOutsideTheApiAreUntouched()
    {
        var response = await Capture(200, "text/html", "<html></html>", path: "/swagger");

        Assert.Equal("<html></html>", response.Body);
        Assert.Equal("text/html", response.ContentType);
    }

    [Fact]
    public async Task PathsThatOnlyStartWithApiAreUntouched()
    {
        var response = await Capture(200, "text/plain", "hello", path: "/apis/other");

        Assert.Equal("hello", response.Body);
    }

    [Fact]
    public async Task PreflightRequestsAreUntouched()
    {
        var response = await Capture(204, null, "", method: HttpMethods.Options);

        Assert.Equal(204, response.StatusCode);
        Assert.Equal("", response.Body);
    }

    [Fact]
    public async Task ResponseBodyIsRestoredWhenTheNextMiddlewareThrows()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/broken";
        var original = new MemoryStream();
        context.Response.Body = original;
        var middleware = new ApiEnvelopeMiddleware(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(original, context.Response.Body);
    }

    private sealed record Captured(int StatusCode, string? ContentType, long? ContentLength, string Body);

    private static async Task<Captured> Capture(
        int status, string? contentType, string body, string path = "/api/things", string method = "GET")
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        context.Request.Method = method;
        context.Request.Path = path;
        var output = new MemoryStream();
        context.Response.Body = output;
        var middleware = new ApiEnvelopeMiddleware(async inner =>
        {
            inner.Response.StatusCode = status;
            if (contentType is not null)
            {
                inner.Response.ContentType = contentType;
            }

            if (body.Length > 0)
            {
                await inner.Response.WriteAsync(body);
            }
        });

        await middleware.InvokeAsync(context);

        return new Captured(
            context.Response.StatusCode,
            context.Response.ContentType,
            context.Response.ContentLength,
            Encoding.UTF8.GetString(output.ToArray()));
    }
}
