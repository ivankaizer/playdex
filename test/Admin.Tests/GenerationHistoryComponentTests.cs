using System.Security.Claims;
using Admin.Features;
using Admin.Infrastructure;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Playdex.Persistence;
using Radzen;
using Xunit;

namespace Admin.Tests;

public sealed class GenerationHistoryComponentTests
{
    [Fact]
    public async Task PendingConfirmationCannotOverwriteAnActiveNavigationRead()
    {
        var confirmation = new TaskCompletionSource<bool>();
        var interactions = new FakeInteractions { Confirmation = confirmation.Task };
        var history = new FakeHistory();
        using var context = Context(history, interactions);
        var component = context.Render<global::Admin.Components.Pages.AiGenerations>();
        Button(component, "Inspect").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("img")));
        var deletion = Button(component, "Delete generation").ClickAsync(new());
        var read = new TaskCompletionSource<GenerationSearchResult>();
        history.PendingSearch = read.Task;
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/ai-generations?status=success");
        navigation.NavigateTo("/ai-generations?status=errors");
        confirmation.SetResult(true);
        await deletion;
        Assert.Equal(0, history.Deleted);
        history.PendingSearch = null;
        read.SetResult(new GenerationSearchResult([], 0, 0));
        component.WaitForAssertion(() => Assert.Equal("errors", history.Scope.Status));
    }

    [Fact]
    public void DetailNavigationDuringLoadingEventuallyOpensLatestGeneration()
    {
        var pending = new TaskCompletionSource<GenerationSearchResult>();
        var history = new FakeHistory { PendingSearch = pending.Task };
        using var context = Context(history, new FakeInteractions());
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/ai-generations?status=success");
        var component = context.Render<global::Admin.Components.Pages.AiGenerations>();
        var id = Guid.NewGuid();
        navigation.NavigateTo($"/ai-generations?status=errors&id={id}");
        history.PendingSearch = null;
        pending.SetResult(new GenerationSearchResult([], 0, 0));
        component.WaitForAssertion(() => Assert.Single(component.FindAll("img")));
        Assert.Equal("errors", history.Scope.Status);
        Assert.Equal($"/ai-generations/{id}/image", component.Find("img").GetAttribute("src"));
    }

    [Fact]
    public void DashboardLinksApplyErrorTimeWindowAndOpenSpecifiedDetails()
    {
        var history = new FakeHistory();
        using var context = Context(history, new FakeInteractions());
        var id = Guid.NewGuid();
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo($"/ai-generations?status=errors&since=2026-09-15T12%3A00%3A00Z&until=2026-09-16T12%3A00%3A00Z&id={id}");
        var component = context.Render<global::Admin.Components.Pages.AiGenerations>();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("img")));
        Assert.Equal("errors", history.Scope.Status);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), history.Scope.Since);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero), history.Scope.Until);
        Assert.Equal($"/ai-generations/{id}/image", component.Find("img").GetAttribute("src"));
    }

    [Fact]
    public void EvidenceAndMalformedResponsesRenderAsTextWithProtectedImageUrls()
    {
        using var context = Context(new FakeHistory(), new FakeInteractions());
        var id = Guid.NewGuid();
        var generation = new AiGeneration { Id=id, Provider="test", Model="test", Timezone="UTC",
            InputText="<script>alert('input')</script>", ImageContentType="image/png",
            ResultJson="{\"Rows\":[]}",ResponsesJson="[{\"HttpStatus\":200,\"Body\":\"<script>output</script>\",\"Truncated\":false}]" };
        var component = context.Render<global::Admin.Components.GenerationDetails>(p=>p.Add(c=>c.Detail,new("user@test.local",generation)));
        Assert.Empty(component.FindAll("script"));
        Assert.Contains("<script>alert('input')</script>",component.Find("pre").TextContent,StringComparison.Ordinal);
        Assert.Equal($"/ai-generations/{id}/image",component.Find("img").GetAttribute("src"));
        Assert.Contains(component.FindAll("summary"), summary=>summary.TextContent.Contains("Attempt 1",StringComparison.Ordinal));
        Assert.Contains("<script>output</script>",component.FindAll("pre")[^1].TextContent,StringComparison.Ordinal);
    }

    [Fact]
    public void HistorySearchInspectAndDeletionRespectConfirmationAndRecoverFromErrors()
    {
        var history = new FakeHistory();
        var interactions = new FakeInteractions();
        using var context = Context(history,interactions);
        var component = context.Render<global::Admin.Components.Pages.AiGenerations>();
        component.WaitForAssertion(()=>Assert.Contains("user@test.local",component.Markup,StringComparison.Ordinal));
        component.Find("input").Change("Hades");
        Button(component,"Search").Click();
        Assert.Equal("Hades",history.Query);
        Button(component,"Inspect").Click();
        component.WaitForAssertion(()=>Assert.Single(component.FindAll("img")));
        Button(component,"Delete generation").Click();
        Assert.Equal(0,history.Deleted);
        interactions.Confirmed=true;
        Button(component,"Delete generation").Click();
        component.WaitForAssertion(()=>Assert.Equal(1,history.Deleted));
        Assert.Empty(component.FindAll("img"));
        history.FailSearch=true;
        Button(component,"Search").Click();
        component.WaitForAssertion(()=>Assert.Contains("Could not load generation history",component.Markup,StringComparison.Ordinal));
        history.FailSearch=false;
        Button(component,"Search").Click();
        component.WaitForAssertion(()=>Assert.DoesNotContain("Could not load generation history",component.Markup,StringComparison.Ordinal));
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<global::Admin.Components.Pages.AiGenerations> component,string text) =>
        component.FindAll("button").Single(button=>button.TextContent.Contains(text,StringComparison.Ordinal));

    private static BunitContext Context(FakeHistory history,FakeInteractions interactions)
    {
        var context=new BunitContext();
        context.JSInterop.Mode=JSRuntimeMode.Loose;
        context.Services.AddRadzenComponents();
        context.Services.AddSingleton<BrowserTimeZone>();
        context.Services.AddSingleton<IGenerationHistoryOperations>(history);
        context.Services.AddSingleton<IAdminInteractions>(interactions);
        var authorization=context.AddAuthorization();
        authorization.SetAuthorized("admin");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier,"1"));
        return context;
    }

    private sealed class FakeHistory : IGenerationHistoryOperations
    {
        private readonly Guid _id=Guid.NewGuid();
        public string? Query {get;private set;}
        public (string? Status, DateTimeOffset? Since, DateTimeOffset? Until) Scope { get; private set; }
        public int Deleted {get;private set;}
        public bool FailSearch {get;set;}
        public Task<GenerationSearchResult>? PendingSearch { get; set; }
        public Task<GenerationSearchResult> Search(ClaimsPrincipal principal,string query,string? status,int page,DateTimeOffset? since=null, DateTimeOffset? until=null, CancellationToken ct=default)
        {
            Query=query;
            Scope=(status,since,until);
            if (PendingSearch is not null) return PendingSearch;
            if(FailSearch) throw new InvalidOperationException("sensitive database detail");
            return Task.FromResult(new GenerationSearchResult([new(_id,"user@test.local",DateTimeOffset.UtcNow,"test","test","success","Hades",true)],1,page));
        }
        public Task<GenerationDetail?> GetDetailsAsync(ClaimsPrincipal principal,Guid id,CancellationToken ct=default)=>
            Task.FromResult<GenerationDetail?>(new("user@test.local",new AiGeneration{Id=id,Provider="test",Model="test",Timezone="UTC",ImageContentType="image/png"}));
        public Task<GenerationImage?> GetImage(ClaimsPrincipal principal,Guid id,CancellationToken ct=default)=>Task.FromResult<GenerationImage?>(null);
        public Task<bool> Delete(ClaimsPrincipal principal,Guid id,CancellationToken ct=default){Deleted++;return Task.FromResult(true);}
    }
    private sealed class FakeInteractions : IAdminInteractions
    {
        public bool Confirmed {get;set;}
        public Task<bool>? Confirmation { get; set; }
        public Task<bool> ConfirmAsync(string message,string title)=>Confirmation ?? Task.FromResult(Confirmed);
        public void Success(string summary,string detail) { }
        public void ShowError(string summary,string detail) { }
    }
}
