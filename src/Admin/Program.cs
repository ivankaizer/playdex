using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Radzen;
using Shared.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var databaseUrl = builder.Configuration["DATABASE_URL"]?.Trim()
    ?? throw new InvalidOperationException("DATABASE_URL is required.");
var dataSource = PostgresServices.CreateDataSource(databaseUrl);
builder.Services.AddSingleton(dataSource);
builder.Services.AddPlaydexDbContextFactory(dataSource);
builder.Services.AddScoped<AdminAuthenticationService>();
builder.Services.AddScoped<AdminOperationGuard>();
builder.Services.AddScoped<Admin.Features.AdminOperations>();
builder.Services.AddScoped<Admin.Features.IAdminOperations>(services =>
    services.GetRequiredService<Admin.Features.AdminOperations>());
builder.Services.AddScoped<Admin.Features.IAdminInteractions, Admin.Features.RadzenAdminInteractions>();
builder.Services.AddRadzenComponents();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "playdex.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = builder.Configuration.GetValue("Admin:CookieLifetime", TimeSpan.FromHours(8));
        options.SlidingExpiration = false;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization(options => options.AddPolicy("Administrator", policy =>
    policy.RequireAuthenticatedUser().RequireClaim("is_admin", "true")));
builder.Services.AddHealthChecks().AddCheck<AdminDatabaseHealthCheck>("database");

var app = builder.Build();
app.UseForwardedHeaders();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapHealthChecks("/health/ready", new HealthCheckOptions());
app.MapPost("/auth/login", async (HttpContext context, AdminAuthenticationService authentication) =>
{
    var form = await context.Request.ReadFormAsync();
    var user = await authentication.ValidateAsync(form["email"].ToString(), form["password"].ToString());
    if (user is null) return Results.Redirect("/login?failed=1");

    var principal = new ClaimsPrincipal(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim("is_admin", "true")
    ], CookieAuthenticationDefaults.AuthenticationScheme));
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    return Results.Redirect("/");
}).AllowAnonymous();
app.MapMethods("/auth/logout", ["GET", "POST"], async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});
app.MapRazorComponents<Admin.Components.App>().AddInteractiveServerRenderMode();
app.Run();

public partial class Program;

public sealed class AdminDatabaseHealthCheck(
    IDbContextFactory<PlaydexDbContext> factory,
    ILogger<AdminDatabaseHealthCheck> logger) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var connected = await db.Database.CanConnectAsync(cancellationToken);
            if (!connected) return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Database unavailable.");
            await db.Users.AsNoTracking().Select(user => user.Id).Take(1).ToListAsync(cancellationToken);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Database and required schema are available.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Admin readiness check failed");
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Database schema is unavailable.");
        }
    }
}

public sealed class AdminAuthenticationService(IDbContextFactory<PlaydexDbContext> factory)
{
    public async Task<User?> ValidateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Email == normalized, cancellationToken);
        return user?.Password is not null && user.IsAdmin && BCrypt.Net.BCrypt.Verify(password, user.Password) ? user : null;
    }
}

public sealed class AdminOperationGuard(IDbContextFactory<PlaydexDbContext> factory)
{
    public async Task<bool> IsAdministratorAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(subject, out var userId)) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.IsAdmin, cancellationToken);
    }
}
