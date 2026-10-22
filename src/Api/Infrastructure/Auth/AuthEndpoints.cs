using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Api.Infrastructure.Http;

namespace Api.Infrastructure.Auth;

public sealed record LoginRequest(string? Email, string? Password);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record MeResponse(long UserId, string Email, bool IsAdmin);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/login", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<LoginRequest>();
            var service = http.RequestServices.GetRequiredService<AuthService>();
            var email = request?.Email?.Trim() ?? "";
            var password = request?.Password ?? "";

            IResult result = email.Length == 0 || password.Length == 0
                ? TypedResults.Problem("Email and password are required.", statusCode: 400)
                : TypedResults.Ok(await service.Login(email, password, http.RequestAborted));

            await result.ExecuteAsync(http);
        }).RequireRateLimiting(RateLimiting.SignIn);

        api.MapPost("/refresh", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<RefreshRequest>();
            var service = http.RequestServices.GetRequiredService<AuthService>();
            var refreshToken = request?.RefreshToken?.Trim() ?? "";

            IResult result = refreshToken.Length == 0
                ? TypedResults.Problem("Refresh token is required.", statusCode: 400)
                : TypedResults.Ok(await service.Refresh(refreshToken, http.RequestAborted));

            await result.ExecuteAsync(http);
        }).RequireRateLimiting(RateLimiting.Refresh);

        api.MapGet("/me", Me).RequireAuthorization();
    }

    public static IServiceCollection AddPlaydexAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<AuthTokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthentication.Scheme, null);
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<AuthTokenService>((options, tokens) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = tokens.ValidationParameters();
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (!string.Equals(
                                context.Principal?.FindFirstValue("type"),
                                "access",
                                StringComparison.Ordinal))
                        {
                            context.Fail("Invalid token.");
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        var detail = context.Request.Headers.Authorization.Count == 0
                            ? "Missing token."
                            : "Invalid token.";
                        await context.Response.WriteAsJsonAsync(
                            new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = detail });
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(
                            new ProblemDetails { Status = 403, Title = "Forbidden", Detail = "Forbidden." });
                    },
                };
            });
        // The default policy stays "an authenticated JWT user"; ApiKey is opted into per endpoint.
        services.AddAuthorization(authorization => authorization.AddPolicy(
            ApiKeyAuthentication.Policy,
            policy => policy.AddAuthenticationSchemes(ApiKeyAuthentication.Scheme).RequireAuthenticatedUser()));
        return services;
    }

    private static async Task<IResult> Me(
        ClaimsPrincipal principal,
        AuthRepository repository,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var user = await repository.FindById(userId, ct);
        if (user is null)
            return TypedResults.Problem("User not found.", statusCode: 404);

        return TypedResults.Ok(new MeResponse(user.Id, user.Email, user.IsAdmin));
    }
}
