using System.Globalization;
using System.Security.Claims;
using Admin.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Playdex.Persistence;
using Radzen;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var databaseUrl = builder.Configuration["DATABASE_URL"]?.Trim()
    ?? throw new InvalidOperationException("DATABASE_URL is required.");
var dataSource = PostgresServices.CreateDataSource(databaseUrl);
builder.Services.AddSingleton(dataSource);
builder.Services.AddPlaydexDbContextFactory(dataSource);
builder.Services.AddScoped<AdminAuthenticationService>();
builder.Services.AddScoped<AdminOperationGuard>();
builder.Services.AddScoped<Admin.Features.IGenerationHistoryOperations, Admin.Features.GenerationHistoryOperations>();
builder.Services.AddScoped<BrowserTimeZone>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<Admin.Features.IDashboardOperations, Admin.Features.DashboardOperations>();
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
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
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
app.MapStaticAssets();
app.MapRazorComponents<Admin.Components.App>().AddInteractiveServerRenderMode();
app.MapGet("/ai-generations/{id:guid}/image", async (Guid id, HttpContext context,
    Admin.Features.IGenerationHistoryOperations history) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    try
    {
        var image = await history.GetImage(context.User, id, context.RequestAborted);
        return image is null ? Results.NotFound() : Results.File(image.Bytes, image.ContentType);
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
}).RequireAuthorization("Administrator");
app.Run();

public partial class Program;
