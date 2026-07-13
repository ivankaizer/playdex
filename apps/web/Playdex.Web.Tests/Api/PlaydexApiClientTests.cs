using System.Net;
using System.Text;
using System.Text.Json;
using Playdex.Web.Api;
using Playdex.Web.Auth;
using Playdex.Web.Browser;
using Playdex.Web.Models;
using Xunit;

namespace Playdex.Web.Tests.Api;

public sealed class PlaydexApiClientTests
{
    [Fact]
    public async Task SendAsync_DeserializesRawNullableDtoWithoutAnEnvelope()
    {
        using var handler = new RecordingHandler((_, _) => Json(
            HttpStatusCode.OK,
            """
            {
              "accountId": 9007199254740991,
              "session": {
                "sessionId": 9223372036854775806,
                "startedAt": "2026-07-13T12:34:56+00:00",
                "endedAt": null,
                "gameId": "aZ9",
                "platform": "steam",
                "game": "Portal 2",
                "capsuleUrl": null,
                "ignored": false,
                "mappedPlatform": null,
                "mappedSource": null
              }
            }
            """));
        var client = CreateClient(handler);

        var response = await client.SendAsync<AccountCurrentSessionResponse>("/sessions/current");

        Assert.Equal(9_007_199_254_740_991, response.AccountId);
        Assert.Equal(9_223_372_036_854_775_806, response.Session!.SessionId);
        Assert.Equal("aZ9", response.Session.GameId);
        Assert.Null(response.Session.EndedAt);
        Assert.Null(response.Session.CapsuleUrl);
    }

    [Fact]
    public async Task Authenticated401_RefreshesWithoutBearerPersistsTokensAndRetriesFreshRequest()
    {
        var storage = new MemoryBrowserStorage(
            ("playdex.token", "old-access"),
            ("playdex.refresh", "old-refresh"));
        using var handler = new RecordingHandler(
            (_, _) => Json(HttpStatusCode.Unauthorized, """{"status":401,"detail":"expired"}"""),
            (_, _) => Json(HttpStatusCode.OK, """{"accessToken":"new-access","refreshToken":"new-refresh"}"""),
            (_, _) => Json(HttpStatusCode.OK, """{"userId":42,"email":"player@example.com","isAdmin":false}"""));
        var client = CreateClient(handler, storage);
        var body = new LoginRequest("player@example.com", "secret");

        var response = await client.SendAsync<MeResponse>("/me", HttpMethod.Post, body);

        Assert.Equal(42, response.UserId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("Bearer old-access", handler.Requests[0].Authorization);
        Assert.Null(handler.Requests[1].Authorization);
        Assert.Equal("Bearer new-access", handler.Requests[2].Authorization);
        Assert.Equal("https://app.test/api/refresh", handler.Requests[1].Uri);
        Assert.Equal("{\"refreshToken\":\"old-refresh\"}", handler.Requests[1].Body);
        Assert.Equal(handler.Requests[0].BodyBytes, handler.Requests[2].BodyBytes);
        Assert.NotSame(handler.Requests[0].Request, handler.Requests[2].Request);
        Assert.NotSame(handler.Requests[0].Content, handler.Requests[2].Content);
        Assert.Equal("new-access", await storage.GetAsync("playdex.token"));
        Assert.Equal("new-refresh", await storage.GetAsync("playdex.refresh"));
    }

    [Fact]
    public async Task Retry401_DoesNotRefreshAgainOrInvokeAuthFailure()
    {
        var callbackCount = 0;
        var storage = new MemoryBrowserStorage(
            ("playdex.token", "old-access"),
            ("playdex.refresh", "old-refresh"));
        using var handler = new RecordingHandler(
            (_, _) => Json(HttpStatusCode.Unauthorized, """{"detail":"original"}"""),
            (_, _) => Json(HttpStatusCode.OK, """{"accessToken":"new-access","refreshToken":"new-refresh"}"""),
            (_, _) => Json(HttpStatusCode.Unauthorized, """{"detail":"retry"}"""));
        var client = CreateClient(handler, storage);

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.SendAsync<object>(
            "/me",
            onAuthFailure: () =>
            {
                callbackCount++;
                return Task.CompletedTask;
            }));

        Assert.Equal("retry", exception.Message);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(0, callbackCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("failed")]
    [InlineData("malformed")]
    [InlineData("missing-access")]
    [InlineData("missing-refresh")]
    public async Task RefreshFailure_InvokesCallbackOnceAndThrowsOriginal401(string failure)
    {
        var callbackCount = 0;
        var storage = failure == "missing"
            ? new MemoryBrowserStorage(("playdex.token", "old-access"))
            : new MemoryBrowserStorage(("playdex.token", "old-access"), ("playdex.refresh", "old-refresh"));
        var responders = new List<Responder>
        {
            (_, _) => Task.FromResult(Json(
                HttpStatusCode.Unauthorized,
                """{"status":401,"detail":"original detail"}""")),
        };
        if (failure != "missing")
        {
            responders.Add(failure switch
            {
                "failed" => (_, _) => Task.FromResult(Json(
                    HttpStatusCode.InternalServerError,
                    """{"detail":"refresh failed"}""")),
                "malformed" => (_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{")),
                "missing-access" => (_, _) => Task.FromResult(Json(
                    HttpStatusCode.OK,
                    """{"refreshToken":"new-refresh"}""")),
                _ => (_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"accessToken":"new-access"}""")),
            });
        }

        using var handler = new RecordingHandler([.. responders]);
        var client = CreateClient(handler, storage);

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.SendAsync<object>(
            "/me",
            onAuthFailure: () =>
            {
                callbackCount++;
                return Task.CompletedTask;
            }));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal("original detail", exception.Message);
        Assert.Equal(1, callbackCount);
        Assert.Equal(failure == "missing" ? 1 : 2, handler.Requests.Count);
    }

    [Fact]
    public async Task Unauthenticated401_OmitsBearerAndDoesNotRefreshOrInvokeCallback()
    {
        var callbackCount = 0;
        var storage = new MemoryBrowserStorage(
            ("playdex.token", "access"),
            ("playdex.refresh", "refresh"));
        using var handler = new RecordingHandler((_, _) =>
            Json(HttpStatusCode.Unauthorized, """{"detail":"bad credentials"}"""));
        var client = CreateClient(handler, storage);

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.SendAsync<object>(
            "/login",
            HttpMethod.Post,
            new LoginRequest("a@b.test", "bad"),
            authenticated: false,
            onAuthFailure: () =>
            {
                callbackCount++;
                return Task.CompletedTask;
            }));

        Assert.Equal("bad credentials", exception.Message);
        Assert.Single(handler.Requests);
        Assert.Null(handler.Requests[0].Authorization);
        Assert.Equal(0, callbackCount);
    }

    [Theory]
    [InlineData("https://app.test/api", "/me", "https://app.test/api/me")]
    [InlineData("https://app.test/api/", "me", "https://app.test/api/me")]
    [InlineData("https://app.test/api///", "/refresh", "https://app.test/api/refresh")]
    [InlineData("https://app.test/api", "/sessions?title=a%20b&page=2", "https://app.test/api/sessions?title=a%20b&page=2")]
    [InlineData("https://ignored.test/api", "https://other.test/me?q=a%20b", "https://other.test/me?q=a%20b")]
    [InlineData("https://ignored.test/api", "http://other.test/me", "http://other.test/me")]
    public async Task SendAsync_BuildsExactApiUri(string apiBase, string path, string expected)
    {
        using var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, "null"));
        var client = CreateClient(handler, apiBase: apiBase);

        await client.SendAsync<object?>(path, authenticated: false);

        Assert.Equal(expected, handler.Requests[0].Uri);
    }

    [Fact]
    public async Task RequestBodies_UseExactWebCamelCaseIncludingAdminUser()
    {
        using var handler = new RecordingHandler(
            (_, _) => Json(HttpStatusCode.OK, "null"),
            (_, _) => Json(HttpStatusCode.OK, "null"));
        var client = CreateClient(handler);

        await client.SendAsync<object?>(
            "/manual/entries",
            HttpMethod.Post,
            new ManualEntryCreateRequest("2026-07-13", 45, null, 123, "Game", "pc", null),
            authenticated: false);
        await client.SendAsync<object?>(
            "/admin/credentials/7",
            HttpMethod.Put,
            new AdminCredentialUpdateRequest("key", "account-user"),
            authenticated: false);

        using var manual = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal(45, manual.RootElement.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(123, manual.RootElement.GetProperty("igdbId").GetInt32());
        Assert.True(manual.RootElement.TryGetProperty("platformKey", out _));
        Assert.False(manual.RootElement.TryGetProperty("DurationMinutes", out _));

        using var admin = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("account-user", admin.RootElement.GetProperty("user").GetString());
        Assert.False(admin.RootElement.TryGetProperty("accountUser", out _));
    }

    [Theory]
    [InlineData("{\"status\":400,\"detail\":\"Problem detail\",\"correlationId\":\"abc\"}", "Problem detail")]
    [InlineData("{\"detail\":\"JWT detail\",\"title\":\"Unauthorized\"}", "JWT detail")]
    [InlineData("{\"error\":{\"error\":\"legacy envelope\"}}", "Request failed.")]
    [InlineData("{\"detail\":\"\"}", "Request failed.")]
    [InlineData("not-json", "Request failed.")]
    [InlineData("", "Request failed.")]
    public async Task ApiException_UsesOnlyRootDetailOrFallback(string payload, string expected)
    {
        using var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.BadRequest, payload));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            client.SendAsync<object>("/broken", authenticated: false));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(expected, exception.Message);
    }

    [Fact]
    public async Task CancellationBeforeInitialRequest_Propagates()
    {
        using var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, "null");
        });
        var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SendAsync<object?>("/me", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task CancellationDuringRefresh_PropagatesWithoutAuthFailureCallback()
    {
        var callbackCount = 0;
        using var cancellation = new CancellationTokenSource();
        var storage = new MemoryBrowserStorage(
            ("playdex.token", "old-access"),
            ("playdex.refresh", "old-refresh"));
        using var handler = new RecordingHandler(
            (_, _) => Json(HttpStatusCode.Unauthorized, """{"detail":"expired"}"""),
            (_, cancellationToken) =>
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return Json(HttpStatusCode.OK, "null");
            });
        var client = CreateClient(handler, storage);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync<object?>(
            "/me",
            onAuthFailure: () =>
            {
                callbackCount++;
                return Task.CompletedTask;
            },
            cancellationToken: cancellation.Token));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public async Task EmptyAccessToken_OmitsBearerHeader()
    {
        var storage = new MemoryBrowserStorage(("playdex.token", ""));
        using var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, "null"));
        var client = CreateClient(handler, storage);

        await client.SendAsync<object?>("/me");

        Assert.Null(handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task ManualSearchModel_UsesStringIgdbIdWhileMappingRequestUsesNullableInt()
    {
        using var handler = new RecordingHandler((_, _) => Json(
            HttpStatusCode.OK,
            """{"query":"portal","igdbAvailable":true,"results":[{"source":"igdb","gameId":null,"igdbId":"00123","title":"Portal","platform":null,"platformId":null,"capsuleUrl":null}]}"""));
        var client = CreateClient(handler);

        var response = await client.SendAsync<ManualMappingSearchResponse>("/manual/search?query=portal");
        var request = new ManualMapInputRequest(null, 123, null, null, null, null);

        Assert.Equal("00123", response.Results[0].IgdbId);
        Assert.Equal(123, request.IgdbId);
    }

    private static PlaydexApiClient CreateClient(
        RecordingHandler handler,
        MemoryBrowserStorage? storage = null,
        string apiBase = "https://app.test/api/")
    {
        storage ??= new MemoryBrowserStorage();
        return new PlaydexApiClient(new HttpClient(handler), new AuthSession(storage), apiBase);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private delegate Task<HttpResponseMessage> Responder(CapturedRequest request, CancellationToken cancellationToken);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Responder> _responders;

        public RecordingHandler(params Func<CapturedRequest, CancellationToken, HttpResponseMessage>[] responders)
            : this(responders.Select<Func<CapturedRequest, CancellationToken, HttpResponseMessage>, Responder>(
                responder => (request, cancellationToken) => Task.FromResult(responder(request, cancellationToken))).ToArray())
        {
        }

        public RecordingHandler(params Responder[] responders) => _responders = new Queue<Responder>(responders);

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var bodyBytes = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var captured = new CapturedRequest(
                request,
                request.Content,
                request.RequestUri!.AbsoluteUri,
                request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null,
                bodyBytes,
                bodyBytes is null ? null : Encoding.UTF8.GetString(bodyBytes));
            Requests.Add(captured);
            return await _responders.Dequeue()(captured, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpRequestMessage Request,
        HttpContent? Content,
        string Uri,
        string? Authorization,
        byte[]? BodyBytes,
        string? Body);

    private sealed class MemoryBrowserStorage(params (string Key, string? Value)[] values) : IBrowserStorage
    {
        private readonly Dictionary<string, string?> _values = values.ToDictionary(item => item.Key, item => item.Value);

        public ValueTask<string?> GetAsync(string key)
        {
            _values.TryGetValue(key, out var value);
            return ValueTask.FromResult(value);
        }

        public ValueTask SetAsync(string key, string value)
        {
            _values[key] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key)
        {
            _values.Remove(key);
            return ValueTask.CompletedTask;
        }
    }
}
