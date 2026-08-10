using System.Net;
using System.Text.Json;
using Api.Infrastructure.Http;
using Api.Modules.Notifications;
using Microsoft.AspNetCore.Http;
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
}
