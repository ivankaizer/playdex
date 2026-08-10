using System.Net;
using System.Text;
using Api.Modules.Notifications;
using Xunit;

namespace Api.Tests;

public sealed class AppriseClientTests
{
    [Fact]
    public async Task SendsTextAndMultipartAttachmentToConfigurationKey()
    {
        var handler = new RecordingHandler(
            Json("{}"),
            Json("{}"));
        var client = CreateClient(handler);

        await client.SendMessageAsync("42", "hello", CancellationToken.None);
        await client.SendAttachmentAsync("42", [1, 2, 3], "caption", CancellationToken.None);

        Assert.EndsWith("/notify/telegram_42", handler.Requests[0].Uri);
        Assert.Contains("\"body\":\"hello\"", handler.Requests[0].Body);
        Assert.EndsWith("/notify/telegram_42", handler.Requests[1].Uri);
        Assert.StartsWith("multipart/form-data", handler.Requests[1].ContentType);
        Assert.Contains("notification.webp", handler.Requests[1].Body);
    }

    [Fact]
    public async Task MissingConfigurationIsCreatedFromBotTokenAndDeliveryIsRetried()
    {
        var handler = new RecordingHandler(
            Json("{}", HttpStatusCode.NoContent),
            Json("{}"),
            Json("{}"));
        var client = CreateClient(handler, "123456789:abcdefghijklmnopqrstuvwxyz_ABCDE");

        await client.SendMessageAsync("-10042", "hello", CancellationToken.None);

        Assert.EndsWith("/notify/telegram_n10042", handler.Requests[0].Uri);
        Assert.EndsWith("/add/telegram_n10042", handler.Requests[1].Uri);
        Assert.Contains(
            "tgram://123456789:abcdefghijklmnopqrstuvwxyz_ABCDE/-10042/",
            handler.Requests[1].Body);
        Assert.EndsWith("/notify/telegram_n10042", handler.Requests[2].Uri);
    }

    [Fact]
    public async Task AppriseDeliveryFailureIsRaised()
    {
        var handler = new RecordingHandler(Json("{}", HttpStatusCode.FailedDependency));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<AppriseApiException>(() =>
            client.SendMessageAsync("42", "hello", CancellationToken.None));

        Assert.Contains("HTTP 424", exception.Message);
    }

    [Fact]
    public async Task AutomaticConfigurationRequiresAdministratorBotToken()
    {
        var client = CreateClient(new RecordingHandler(Json("{}", HttpStatusCode.NoContent)));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendMessageAsync("42", "hello", CancellationToken.None));
        Assert.Contains("TelegramBotToken", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("invalid")]
    [InlineData("12.5")]
    public async Task InvalidTelegramChatIdIsRejected(string chatId)
    {
        var client = CreateClient(new RecordingHandler(Json("{}")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SendMessageAsync(chatId, "hello", CancellationToken.None));
    }

    private static AppriseClient CreateClient(
        HttpMessageHandler handler,
        string? telegramBotToken = null) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://apprise.test/") },
        new AppriseOptions
        {
            BaseUrl = "https://apprise.test/",
            TelegramBotToken = telegramBotToken,
        });

    private static HttpResponseMessage Json(
        string payload,
        HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.ToString(),
                request.Content?.Headers.ContentType?.ToString() ?? "",
                request.Content is null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(string Uri, string ContentType, string Body);
}
