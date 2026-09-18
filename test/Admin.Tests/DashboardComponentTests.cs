using System.Security.Claims;
using Admin.Features;
using Admin.Infrastructure;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Xunit;

namespace Admin.Tests;

public sealed class DashboardComponentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadingFailureRetryAndRevocationPreserveOnlyAuthorizedData()
    {
        var pending = new TaskCompletionSource<DashboardSnapshot>();
        var fake = new FakeDashboard { Handler = (_, _, _) => pending.Task };
        using var context = Context(fake, new TestClock(Now));
        var component = context.Render<global::Admin.Components.Pages.Home>();
        Assert.Contains("rz-progressbar", component.Markup, StringComparison.Ordinal);
        pending.SetResult(Snapshot());
        component.WaitForAssertion(() => Assert.Contains("2h 50m", component.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(component.FindAll("button"), b => b.TextContent.Contains("Stop", StringComparison.Ordinal));
        fake.Handler = (_, _, _) => throw new InvalidOperationException("secret db error");
        await Button(component, "Refresh").ClickAsync(new());
        Assert.Contains("Could not refresh", component.Markup, StringComparison.Ordinal);
        Assert.Contains("owner@test.local", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("secret db error", component.Markup, StringComparison.Ordinal);
        fake.Handler = (_, _, _) => Task.FromResult(Snapshot() with { LiveSessions = [] });
        await Button(component, "Retry").ClickAsync(new());
        Assert.Contains("No live sessions right now", component.Markup, StringComparison.Ordinal);
        fake.Handler = (_, _, _) => throw new UnauthorizedAccessException();
        await Button(component, "Refresh").ClickAsync(new());
        Assert.Contains("Administrator access is required", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard-counter", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimerPreservesAppliedFiltersPreventsOverlapAndCancelsPendingReads()
    {
        var clock = new TestClock(Now);
        var fake = new FakeDashboard();
        using var context = Context(fake, clock);
        var component = context.Render<global::Admin.Components.Pages.Home>();
        component.WaitForAssertion(() => Assert.Equal(1, fake.Calls));
        component.Find("input[aria-label='Search live sessions']").Change("  owner  ");
        await Button(component, "Search").ClickAsync(new());
        Assert.Equal("owner", fake.Query);
        // Editing the input alone must not change periodic refresh filters.
        component.Find("input[aria-label='Search live sessions']").Change("unsubmitted");
        var pending = new TaskCompletionSource<DashboardSnapshot>();
        fake.Handler = (_, _, _) => pending.Task;
        await component.InvokeAsync(() => clock.Tick());
        component.WaitForAssertion(() => Assert.Equal(3, fake.Calls));
        await component.InvokeAsync(() => clock.Tick());
        Assert.Equal(3, fake.Calls);
        Assert.Equal("owner", fake.Query);
        Assert.False(fake.Token.IsCancellationRequested);
        var disposal = component.Instance.DisposeAsync().AsTask();
        Assert.True(fake.Token.IsCancellationRequested);
        pending.SetCanceled(fake.Token);
        await disposal;
        clock.Tick();
        Assert.Equal(3, fake.Calls);
    }

    [Fact]
    public void CounterAndGenerationLinksCarryTheirScope()
    {
        using var context = Context(new FakeDashboard(), new TestClock(Now));
        var component = context.Render<global::Admin.Components.Pages.Home>();
        component.WaitForAssertion(() => Assert.Equal(4, component.FindAll(".dashboard-counter").Count));
        var links = component.FindAll(".dashboard-counter").Select(a => a.GetAttribute("href")).ToArray();
        Assert.Equal("/sessions?status=live", links[0]);
        Assert.Contains("from=", links[2], StringComparison.Ordinal);
        Assert.Contains("status=errors&since=", links[3], StringComparison.Ordinal);
        Assert.Contains(component.FindAll("a"), a => a.GetAttribute("href") == $"/ai-generations?id={Guid.Empty}");
    }

    [Theory]
    [InlineData("2026-03-29T12:00:00Z", "2026-03-28T23:00:00Z", 23)]
    [InlineData("2026-10-25T12:00:00Z", "2026-10-24T22:00:00Z", 25)]
    public async Task BrowserDayUsesLocalMidnightAndDaylightSaving(string now, string start, int hours)
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<string>("Playdex.browserTimeZone").SetResult("Europe/Warsaw");
        var time = new BrowserTimeZone(context.JSInterop.JSRuntime);
        await time.EnsureLoadedAsync();
        var range = time.DayRange(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture), range.Start);
        Assert.Equal(hours, (range.End - range.Start).TotalHours);
    }

    [Theory]
    [InlineData("Africa/Cairo", "2026-04-23T12:00:00Z", "2026-04-22T22:00:00Z", 24)]
    [InlineData("Africa/Cairo", "2026-04-24T12:00:00Z", "2026-04-23T22:00:00Z", 23)]
    [InlineData("Africa/Cairo", "2026-10-30T12:00:00Z", "2026-10-29T22:00:00Z", 24)]
    [InlineData("Africa/Cairo", "2026-10-29T12:00:00Z", "2026-10-28T21:00:00Z", 25)]
    [InlineData("America/Havana", "2026-11-01T12:00:00Z", "2026-11-01T04:00:00Z", 25)]
    public async Task BrowserDayHandlesMidnightTransitions(string zone, string now, string start, int hours)
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<string>("Playdex.browserTimeZone").SetResult(zone);
        var time = new BrowserTimeZone(context.JSInterop.JSRuntime);
        await time.EnsureLoadedAsync();
        var range = time.DayRange(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture), range.Start);
        Assert.Equal(hours, (range.End - range.Start).TotalHours);
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<global::Admin.Components.Pages.Home> component, string text) =>
        component.FindAll("button").Single(b => b.TextContent.Trim() == text || b.TextContent.Trim().EndsWith(text, StringComparison.Ordinal));
    private static BunitContext Context(FakeDashboard fake, TestClock clock)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<string>("Playdex.browserTimeZone").SetResult("UTC");
        context.Services.AddRadzenComponents();
        context.Services.AddSingleton<BrowserTimeZone>();
        context.Services.AddSingleton<IDashboardOperations>(fake);
        context.Services.AddSingleton<TimeProvider>(clock);
        var auth = context.AddAuthorization();
        auth.SetAuthorized("admin");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "1"));
        return context;
    }
    private static DashboardSnapshot Snapshot() => new(Now, new(1, 1, 5, 1), [new("switch2", "Nintendo Switch 2")],
        [new(1, "owner@test.local", "Game", null, "switch2", "Nintendo Switch 2", null, Playdex.Persistence.CaptureMethod.Manual, Now.AddMinutes(-170), null, null)], [],
        [new(Guid.Empty, "owner@test.local", "test", "failure", Now)]);
    private sealed class FakeDashboard : IDashboardOperations
    {
        public int Calls { get; private set; }
        public string? Query { get; private set; }
        public CancellationToken Token { get; private set; }
        public Func<string, string?, CancellationToken, Task<DashboardSnapshot>> Handler { get; set; } = (_, _, _) => Task.FromResult(Snapshot());
        public Task<DashboardSnapshot> Read(ClaimsPrincipal principal, string query, string? platform, DateTimeOffset dayStart, DateTimeOffset dayEnd, CancellationToken ct = default)
        { Calls++; Query = query; Token = ct; return Handler(query, platform, ct); }
    }
    // A manually triggered TimeProvider exercises the actual PeriodicTimer without wall-clock sleeps.
    private sealed class TestClock(DateTimeOffset now) : TimeProvider, IDisposable
    {
        private TestTimer? _timer;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => _timer = new(callback, state);
        public void Tick() => _timer?.Tick();
        public void Dispose() => _timer?.Dispose();
        private sealed class TestTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public void Tick() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
