using System.Globalization;
using System.Security.Claims;
using Playdex.Api.Http;

namespace Playdex.Api.Providers;

public static class ProvidersEndpoints
{
    public static void MapProvidersEndpoints(this RouteGroupBuilder api)
    {
        var providers = api.MapGroup("/providers").RequireAuthorization();
        providers.MapGet("", ListProviders);
        providers.MapPut("/status", UpdateStatus);

        api.MapPost("/playnite/key", GetPlayniteKey).RequireAuthorization();
    }

    private static async Task<ApiResponse<ProvidersResponseDto>> ListProviders(
        ClaimsPrincipal principal,
        ProvidersService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        return ApiResponse<ProvidersResponseDto>.Ok(await service.ListProviders(userId, ct));
    }

    private static async Task<ApiResponse<ProviderUpdateResponseDto>> UpdateStatus(
        ProviderUpdateRequest? request,
        ClaimsPrincipal principal,
        ProvidersService service,
        CancellationToken ct)
    {
        if (request is null) throw AppProblem.Validation("Request body is required.");
        var userId = GetUserId(principal);
        return ApiResponse<ProviderUpdateResponseDto>.Ok(
            await service.UpdateProviderStatus(userId, request, ct));
    }

    private static async Task<ApiResponse<PlayniteKeyResponseDto>> GetPlayniteKey(
        ClaimsPrincipal principal,
        ProvidersService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        return ApiResponse<PlayniteKeyResponseDto>.Ok(
            await service.GetOrCreatePlayniteKey(userId, ct));
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
