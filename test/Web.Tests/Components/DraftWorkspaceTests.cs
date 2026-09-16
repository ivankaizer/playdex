using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Web.Infrastructure.Auth;
using Web.Infrastructure.Browser;
using Web.Infrastructure.Components;
using Web.Infrastructure.Http;
using Web.Infrastructure.Models;
using Xunit;

namespace Web.Tests.Components;

public class DraftWorkspaceTests : BunitContext
{
    private static DraftRowDto Row() => new(Guid.NewGuid(), 1, "pending", "Hades", "2026-09-08", 60, "unknown", new("local", "game", null, "Hades", null, null, null), [], true, null);
    private IRenderedComponent<DraftWorkspace> Setup(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond,
        Action? accepted = null,
        bool embedded = false,
        DraftCapabilities? capabilities = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new PlaydexApiClient(new HttpClient(new Handler(respond)), new AuthSession(new Storage()), "https://app.test/api"));
        return Render<DraftWorkspace>(p => p
            .Add(c => c.OnAccepted, () => accepted?.Invoke())
            .Add(c => c.Embedded, embedded)
            .Add(c => c.Capabilities, capabilities));
    }
    private static HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    [Theory]
    [InlineData(30, "for 30 days")]
    [InlineData(0, "until deleted")]
    public void ComposerDisclosesConfiguredAdministratorRetention(int days, string period)
    {
        var cut = Setup(_ => Task.FromResult(Ok(Array.Empty<DraftBatchDto>())), embedded:true,
            capabilities:new(true,"openrouter",10000,10485760,20000000,50,days));
        Assert.Contains("Playdex retains your input, screenshot, and AI results",cut.Markup,StringComparison.Ordinal);
        Assert.Contains(period,cut.Markup,StringComparison.Ordinal);
        Assert.Contains("administrator review",cut.Markup,StringComparison.Ordinal);
        Assert.DoesNotContain("not retained",cut.Markup,StringComparison.Ordinal);
    }

    [Fact] public void ResumeWorksWhenGenerationDisabledAndMissingFieldsBlockAccept()
    {
        var row = Row() with
        {
            Date = null,
            DurationMinutes = null,
            Selection = null,
            Ready = false,
            Warnings =
            [
                "Date: Day and time shown without month or year; date unresolved.",
                "Platform: Platform not indicated in evidence.",
                "Date: enter an unambiguous calendar date.",
                "Platform: Unknown; confirm or select the platform you played on.",
                "Provide a valid date and positive duration.",
                "Platform is Unknown. You can choose a platform or accept Unknown.",
            ],
        };
        var batch = new DraftBatchDto(Guid.NewGuid(), DateTimeOffset.UtcNow, [row]);
        var cut = Setup(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(batch.Id.ToString(), StringComparison.Ordinal) ? Ok(batch) : Ok(new[] { batch })));
        cut.WaitForAssertion(() => Assert.Contains("Resume", cut.Markup));
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resume", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("input[type=date]")));
        Assert.NotNull(cut.Find("nav[aria-label='Breadcrumb']"));
        Assert.Contains("Saved batches", cut.Find("nav[aria-label='Breadcrumb']").TextContent);
        Assert.DoesNotContain("Back to saved batches", cut.Markup);
        Assert.Equal("true", cut.Find("input[data-game-search]").GetAttribute("aria-invalid"));
        Assert.Equal("true", cut.Find("input[type=date]").GetAttribute("aria-invalid"));
        Assert.All(cut.FindAll("input[type=number]"), input => Assert.Equal("true", input.GetAttribute("aria-invalid")));
        Assert.Contains("border-rose-500", cut.Find("input[type=date]").ClassList);
        Assert.Contains("border-rose-500", cut.Find("select").ClassList);
        Assert.DoesNotContain("Platform is Unknown", cut.Markup);
        Assert.DoesNotContain("date unresolved", cut.Markup);
        Assert.DoesNotContain("enter an unambiguous calendar date", cut.Markup);
        Assert.DoesNotContain("Provide a valid date and positive duration", cut.Markup);
        Assert.DoesNotContain("Select a valid date", cut.Markup);
        Assert.True(cut.FindAll("button").Single(b => b.TextContent == "Accept").HasAttribute("disabled"));
    }

    [Fact] public void FailedSaveKeepsEditsAndDoesNotAcceptOrClose()
    {
        var row = Row(); var batch = new DraftBatchDto(Guid.NewGuid(), DateTimeOffset.UtcNow, [row]);
        var cut = Setup(request => Task.FromResult(request.Method == HttpMethod.Patch
            ? new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { detail = "Stale revision" }) }
            : request.RequestUri!.AbsolutePath.EndsWith(batch.Id.ToString(), StringComparison.Ordinal) ? Ok(batch) : Ok(new[] { batch })));
        cut.WaitForAssertion(() => Assert.Contains("Resume", cut.Markup));
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resume", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("input[type=date]")));
        cut.FindAll("input[type=number]")[0].Change("3");
        cut.FindAll("button").Single(b => b.TextContent == "Save edits").Click();
        cut.WaitForAssertion(() => Assert.Contains("Stale revision", cut.Markup));
        Assert.Equal("3", cut.FindAll("input[type=number]")[0].GetAttribute("value"));
        Assert.DoesNotContain("Accepted — session saved", cut.Markup);
    }

    [Fact] public void PartialAcceptanceMarksOnlySuccessfulRowsAndRefreshesSessions()
    {
        var first = Row(); var second = Row();
        var batch = new DraftBatchDto(Guid.NewGuid(), DateTimeOffset.UtcNow, [first, second]);
        var refreshed = false;
        var cut = Setup(request => Task.FromResult(request.Method == HttpMethod.Post
            ? Ok(new DraftAcceptResponse([new(first.Id, 42, null), new(second.Id, null, "IGDB unavailable")]))
            : request.RequestUri!.AbsolutePath.EndsWith(batch.Id.ToString(), StringComparison.Ordinal) ? Ok(batch) : Ok(new[] { batch })), () => refreshed = true);
        cut.WaitForAssertion(() => Assert.Contains("Resume", cut.Markup));
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resume", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("article").Count));
        cut.FindAll("button").Single(b => b.TextContent == "Accept all ready").Click();
        cut.WaitForAssertion(() => Assert.Contains("IGDB unavailable", cut.Markup));
        Assert.True(refreshed);
        Assert.True(cut.FindAll("fieldset")[0].HasAttribute("disabled"));
        Assert.False(cut.FindAll("fieldset")[1].HasAttribute("disabled"));
    }

    [Fact]
    public void CompletingLastRowRemovesBatchFromSavedReview()
    {
        var row = Row();
        var batch = new DraftBatchDto(Guid.NewGuid(), DateTimeOffset.UtcNow, [row]);
        var refreshed = false;
        var listCalls = 0;
        var cut = Setup(request =>
        {
            if (request.Method == HttpMethod.Post)
                return Task.FromResult(Ok(new DraftAcceptResponse([new(row.Id, 42, null)])));
            if (request.RequestUri!.AbsolutePath.EndsWith(batch.Id.ToString(), StringComparison.Ordinal))
                return Task.FromResult(Ok(batch));
            listCalls++;
            return Task.FromResult(Ok(listCalls == 1 ? new[] { batch } : Array.Empty<DraftBatchDto>()));
        }, () => refreshed = true);

        cut.WaitForAssertion(() => Assert.Contains("Resume", cut.Markup));
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resume", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("article")));
        cut.FindAll("button").Single(b => b.TextContent == "Accept all ready").Click();

        cut.WaitForAssertion(() => Assert.Contains("removed from saved review", cut.Markup));
        Assert.True(refreshed);
        Assert.Contains("No saved drafts", cut.Markup);
    }

    [Fact]
    public void EmbeddedWorkspaceUsesAFullPageComposerAndSavedBatchRail()
    {
        var capabilities = new DraftCapabilities(true, "OpenRouter", 10000, 10 * 1024 * 1024, 20_000_000, 50);
        var cut = Setup(
            _ => Task.FromResult(Ok(Array.Empty<DraftBatchDto>())),
            embedded: true,
            capabilities: capabilities);

        cut.WaitForAssertion(() => Assert.Contains("No saved drafts", cut.Markup));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.DoesNotContain(">Close<", cut.Markup);
        Assert.Contains("Create AI batch", cut.Markup);
        Assert.Contains("Saved batches", cut.Markup);
        Assert.NotNull(cut.Find("[data-layout=extended-ai]"));
        Assert.NotNull(cut.Find("aside"));
    }

    [Fact]
    public void GenerationRetriesOneBrowserTransportFailureWithoutChangingTheRequestId()
    {
        var capabilities = new DraftCapabilities(true, "OpenRouter", 10000, 10 * 1024 * 1024, 20_000_000, 50);
        var batch = new DraftBatchDto(Guid.NewGuid(), DateTimeOffset.UtcNow, [Row()]);
        var postCalls = 0;
        var requestIds = new List<Guid>();
        JSInterop.Setup<string>("playdex.drafts.timezone").SetResult("Europe/Warsaw");
        var cut = Setup(async request =>
        {
            if (request.Method != HttpMethod.Post) return Ok(Array.Empty<DraftBatchDto>());
            postCalls++;
            var body = await request.Content!.ReadAsStringAsync();
            requestIds.Add(body
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .First(id => id != Guid.Empty));
            if (postCalls == 1) throw new HttpRequestException("TypeError: Failed to fetch");
            return Ok(batch);
        }, embedded: true, capabilities: capabilities);

        cut.WaitForAssertion(() => Assert.Contains("Create AI batch", cut.Markup));
        cut.Find("textarea").Input("Splatoon Raiders for 1 hour 10 minutes on the 27th");
        cut.FindAll("button").Single(button => button.TextContent.Contains("Generate drafts")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Review extracted sessions", cut.Markup));
        Assert.Equal(2, postCalls);
        Assert.Equal(requestIds[0], requestIds[1]);
    }

    [Fact]
    public async Task FailedGenerationCanRetryTheOriginalTextAndScreenshot()
    {
        var requests = new List<(Guid Id, string Text, byte[] Image)>();
        var batch = new DraftBatchDto(Guid.NewGuid(), DateTimeOffset.UtcNow, [Row()]);
        JSInterop.Setup<string>("playdex.drafts.timezone").SetResult("Europe/Warsaw");
        var cut = Setup(async request =>
        {
            if (request.Method != HttpMethod.Post) return Ok(Array.Empty<DraftBatchDto>());
            requests.Add(await ReadGeneration(request));
            return requests.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = JsonContent.Create(new { detail = "Invalid extraction" }) }
                : Ok(batch);
        }, embedded: true, capabilities: new(true, "OpenRouter", 10000, 10485760, 20000000, 50));
        await cut.InvokeAsync(() => cut.Instance.PasteImage("data:image/png;base64,AQID", "image/png"));
        cut.Find("textarea").Input("Hades yesterday for an hour");
        cut.FindAll("button").Single(button => button.TextContent.Contains("Generate drafts", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains("Invalid extraction", cut.Markup));
        cut.Find("textarea").Input("A different description");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry generation").Click();
        cut.WaitForAssertion(() => Assert.Contains("Review extracted sessions", cut.Markup));
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0].Id, requests[1].Id);
        Assert.All(requests, request => Assert.Equal("Hades yesterday for an hour", request.Text));
        Assert.All(requests, request => Assert.Equal(new byte[] { 1, 2, 3 }, request.Image));
    }

    [Fact]
    public async Task RegenerationGetsAFreshResultAndFailedRegenerationKeepsThePreviousBatch()
    {
        var requests = new List<(Guid Id, string Text, byte[] Image)>();
        var first = new DraftBatchDto(Guid.NewGuid(), DateTimeOffset.UtcNow, [Row()]);
        var second = new DraftBatchDto(Guid.NewGuid(), DateTimeOffset.UtcNow, [Row() with { Title = "Hades II" }]);
        JSInterop.Setup<string>("playdex.drafts.timezone").SetResult("Europe/Warsaw");
        var cut = Setup(async request =>
        {
            if (request.Method != HttpMethod.Post) return Ok(Array.Empty<DraftBatchDto>());
            requests.Add(await ReadGeneration(request));
            if (requests.Count == 2)
                return new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = JsonContent.Create(new { detail = "Invalid extraction" }) };
            return Ok(requests.Count == 1 ? first : second);
        }, embedded: true, capabilities: new(true, "OpenRouter", 10000, 10485760, 20000000, 50));
        await cut.InvokeAsync(() => cut.Instance.PasteImage("data:image/png;base64,AQID", "image/png"));
        cut.Find("textarea").Input("Hades yesterday for an hour");
        cut.FindAll("button").Single(button => button.TextContent.Contains("Generate drafts", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains("Review extracted sessions", cut.Markup));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Generate again").Click();
        cut.WaitForAssertion(() => Assert.Contains("Invalid extraction", cut.Markup));
        Assert.Equal("Hades", cut.Find("input[data-game-search]").GetAttribute("value"));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry generation").Click();
        cut.WaitForAssertion(() => Assert.Equal("Hades II", cut.Find("input[data-game-search]").GetAttribute("value")));
        Assert.Equal(3, requests.Count);
        Assert.NotEqual(requests[0].Id, requests[1].Id);
        Assert.Equal(requests[1].Id, requests[2].Id);
        Assert.All(requests, request => Assert.Equal("Hades yesterday for an hour", request.Text));
        Assert.All(requests, request => Assert.Equal(new byte[] { 1, 2, 3 }, request.Image));
    }

    [Fact]
    public void ResumedBatchRequestsNewEvidenceInsteadOfReusingAnotherBatchesInput()
    {
        var batch = new DraftBatchDto(Guid.NewGuid(), DateTimeOffset.UtcNow, [Row()]);
        var cut = Setup(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(batch.Id.ToString(), StringComparison.Ordinal)
            ? Ok(batch) : Ok(new[] { batch })), embedded: true, capabilities: new(true, "OpenRouter", 10000, 10485760, 20000000, 50));
        cut.WaitForAssertion(() => Assert.Contains("Resume", cut.Markup));
        cut.FindAll("button").Single(button => button.TextContent.StartsWith("Resume", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains("Review extracted sessions", cut.Markup));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Generate again").Click();
        cut.WaitForAssertion(() => Assert.Contains("Add the original description or screenshot", cut.Markup));
        Assert.NotNull(cut.Find("textarea"));
    }

    private static async Task<(Guid Id, string Text, byte[] Image)> ReadGeneration(HttpRequestMessage request)
    {
        var parts = ((MultipartFormDataContent)request.Content!).ToDictionary(part => part.Headers.ContentDisposition!.Name!.Trim('"'));
        return (Guid.Parse(await parts["requestId"].ReadAsStringAsync()), await parts["text"].ReadAsStringAsync(), await parts["file"].ReadAsByteArrayAsync());
    }

    [Fact]
    public void DraftReviewUsesCompactRowsAndRequiresALocalOrIgdbGameMatch()
    {
        var draft = Row() with { Selection = null, Ready = false };
        var batch = new DraftBatchDto(Guid.NewGuid(), DateTimeOffset.UtcNow, [draft]);
        var local = new ManualMappingSearchResult(
            "local", "saved-game", null, "Hades", "PC", 6, "https://covers.test/local.png");
        var igdb = new ManualMappingSearchResult(
            "igdb", null, "114283", "Hades", "PC", 6, "https://covers.test/igdb.png");
        Uri? searchUri = null;
        var cut = Setup(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/manual/search")
            {
                searchUri = request.RequestUri;
                return Task.FromResult(Ok(new ManualMappingSearchResponse("Hades", true, [local, igdb])));
            }
            if (request.Method == HttpMethod.Patch)
                return Task.FromResult(Ok(draft with { Revision = 2, Selection = igdb, Ready = true }));
            return Task.FromResult(request.RequestUri.AbsolutePath.EndsWith(batch.Id.ToString(), StringComparison.Ordinal)
                ? Ok(batch)
                : Ok(new[] { batch }));
        });

        cut.WaitForAssertion(() => Assert.Contains("Resume", cut.Markup));
        cut.FindAll("button").Single(button => button.TextContent.StartsWith("Resume", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-draft-row]")));
        Assert.NotNull(cut.Find("[data-draft-row-fields]"));
        Assert.NotNull(cut.Find("input[data-game-search]"));
        Assert.DoesNotContain("Game title", cut.Markup);
        Assert.True(cut.FindAll("button").Single(button => button.TextContent == "Accept").HasAttribute("disabled"));

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Find game").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-game-result]").Count));
        Assert.Equal("?query=Hades", searchUri?.Query);
        Assert.Contains("Saved game", cut.Markup);
        Assert.Contains("IGDB", cut.Markup);
        Assert.Equal(2, cut.FindAll("[data-game-result] img").Count);

        cut.FindAll("[data-game-result]")[1].Click();

        cut.WaitForAssertion(() => Assert.Contains("Matched from IGDB", cut.Markup));
        Assert.False(cut.FindAll("button").Single(button => button.TextContent == "Accept").HasAttribute("disabled"));
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
    private sealed class Storage : IBrowserStorage
    {
        public ValueTask<string?> GetAsync(string key) => ValueTask.FromResult<string?>(null);
        public ValueTask SetAsync(string key, string value) => ValueTask.CompletedTask;
        public ValueTask RemoveAsync(string key) => ValueTask.CompletedTask;
    }
}
