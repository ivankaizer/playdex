using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Playdex.Api.Http;

namespace Playdex.Api.Auth;

public sealed record LoginRequest(string? Email, string? Password);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record MeResponse(long UserId, string Email, bool IsAdmin);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/login", Login);
        api.MapPost("/refresh", Refresh);
        api.MapGet("/me", Me).RequireAuthorization();
    }

    public static IServiceCollection AddPlaydexAuthentication(
        this IServiceCollection services,
        AuthTokenService tokens)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
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
                        var message = context.Request.Headers.Authorization.Count == 0
                            ? "Missing token."
                            : "Invalid token.";
                        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(message));
                    },
                    OnForbidden = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Forbidden."));
                    },
                };
            });
        services.AddAuthorization();
        return services;
    }

    private static async Task<ApiResponse<LoginResult>> Login(
        LoginRequest? request,
        AuthService service,
        CancellationToken cancellationToken)
    {
        var email = request?.Email?.Trim() ?? "";
        var password = request?.Password ?? "";
        if (email.Length == 0 || password.Length == 0)
        {
            throw AppProblem.Validation("Email and password are required.");
        }

        return ApiResponse<LoginResult>.Ok(
            await service.Login(email, password, cancellationToken));
    }

    private static async Task<ApiResponse<RefreshResult>> Refresh(
        RefreshRequest? request,
        AuthService service,
        CancellationToken cancellationToken)
    {
        var refreshToken = request?.RefreshToken?.Trim() ?? "";
        if (refreshToken.Length == 0)
        {
            throw AppProblem.Validation("Refresh token is required.");
        }

        return ApiResponse<RefreshResult>.Ok(
            await service.Refresh(refreshToken, cancellationToken));
    }

    private static async Task<ApiResponse<MeResponse>> Me(
        ClaimsPrincipal principal,
        IAuthRepository repository,
        CancellationToken cancellationToken)
    {
        var subject = principal.FindFirstValue("sub");
        if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            throw AppProblem.Unauthorized("Invalid token.");
        }

        var user = await repository.FindById(userId, cancellationToken);
        if (user is null)
        {
            throw AppProblem.NotFound("User not found.");
        }

        return ApiResponse<MeResponse>.Ok(new MeResponse(user.Id, user.Email, user.IsAdmin));
    }
}
