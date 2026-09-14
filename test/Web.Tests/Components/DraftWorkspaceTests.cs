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
        var cut = Setup(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(batch.Id.ToString()) ? Ok(batch) : Ok(new[] { batch })));
        cut.WaitForAssertion(() => Assert.Contains("Resume", cut.Markup));
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resume")).Click();
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
            : request.RequestUri!.AbsolutePath.EndsWith(batch.Id.ToString()) ? Ok(batch) : Ok(new[] { batch })));
        cut.WaitForAssertion(() => Assert.Contains("Resume", cut.Markup));
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resume")).Click();
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
            : request.RequestUri!.AbsolutePath.EndsWith(batch.Id.ToString()) ? Ok(batch) : Ok(new[] { batch })), () => refreshed = true);
        cut.WaitForAssertion(() => Assert.Contains("Resume", cut.Markup));
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resume")).Click();
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
