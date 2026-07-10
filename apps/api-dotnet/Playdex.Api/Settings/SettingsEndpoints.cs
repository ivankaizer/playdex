using System.Globalization;
using System.Security.Claims;
using Playdex.Api.Http;

namespace Playdex.Api.Settings;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/settings").RequireAuthorization();
        group.MapGet("", GetSettings);
        group.MapPut("", UpdateSettings);
    }

    private static async Task<ApiResponse<UserSettingsResponseDto>> GetSettings(
        ClaimsPrincipal principal,
        SettingsService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        return ApiResponse<UserSettingsResponseDto>.Ok(await service.GetSettings(userId, ct));
    }

    private static async Task<ApiResponse<UserSettingsResponseDto>> UpdateSettings(
        UpdateSettingsRequest? request,
        ClaimsPrincipal principal,
        SettingsService service,
        CancellationToken ct)
    {
        if (request is null)
        {
            throw AppProblem.Validation("Request body is required.");
        }

        var userId = GetUserId(principal);
        return ApiResponse<UserSettingsResponseDto>.Ok(
            await service.UpdateSettings(userId, request, ct));
    }

    private static long GetUserId(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub");
        if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            throw AppProblem.Unauthorized("Invalid token.");
        }
        return userId;
    }
}
