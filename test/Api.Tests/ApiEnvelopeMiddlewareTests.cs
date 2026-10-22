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
}
