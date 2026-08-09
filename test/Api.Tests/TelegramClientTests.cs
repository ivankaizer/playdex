using System.Net;
using System.Text;
using Api.Modules.Telegram;
using Xunit;

namespace Api.Tests;

public sealed class TelegramClientTests
{
    [Fact]
    public async Task SendsTextAndMultipartPhotoAndRetainsNonMessageUpdates()
    {
        var handler = new RecordingHandler(
            Json("{\"ok\":true,\"result\":{}}"),
            Json("{\"ok\":true,\"result\":{}}"),
            Json("{\"ok\":true,\"result\":[{\"update_id\":7}]}"));
        var client = CreateClient(handler);

        await client.SendMessageAsync(42, "hello", CancellationToken.None);
        await client.SendPhotoAsync(42, [1, 2, 3], "caption", CancellationToken.None);
        var updates = await client.GetUpdatesAsync(7, CancellationToken.None);

        Assert.EndsWith("/bottest-token/sendMessage", handler.Requests[0].Uri);
        Assert.Contains("\"chat_id\":42", handler.Requests[0].Body);
        Assert.EndsWith("/bottest-token/sendPhoto", handler.Requests[1].Uri);
        Assert.StartsWith("multipart/form-data", handler.Requests[1].ContentType);
        Assert.Contains("notification.webp", handler.Requests[1].Body);
        var update = Assert.Single(updates);
        Assert.Equal(7, update.UpdateId);
        Assert.Null(update.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"ok\":false,\"description\":\"chat not found\"}")]
    [InlineData(HttpStatusCode.Unauthorized, "{\"ok\":false,\"description\":\"bad token\"}")]
    public async Task BotApiFailureEnvelopeIsRaised(
        HttpStatusCode status,
        string payload)
    {
        var handler = new RecordingHandler(Json(payload, status));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<TelegramApiException>(() =>
            client.SendMessageAsync(42, "hello", CancellationToken.None));

        Assert.Contains(status == HttpStatusCode.OK ? "chat not found" : "bad token", exception.Message);
    }

    private static TelegramClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.test/") },
        "test-token");

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
