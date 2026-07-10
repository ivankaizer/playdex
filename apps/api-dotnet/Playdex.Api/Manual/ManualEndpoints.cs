using System.Globalization;
using System.Security.Claims;
using Playdex.Api.Http;

namespace Playdex.Api.Manual;

public static class ManualEndpoints
{
    public static void MapManualEndpoints(this RouteGroupBuilder api)
    {
        var manual = api.MapGroup("/manual").RequireAuthorization();

        manual.MapGet("/inputs", ListInputs);
        manual.MapPost("/sessions/start", StartSession);
        manual.MapPost("/sessions/stop", StopSession);
        manual.MapGet("/search", SearchMapping);
        manual.MapPost("/entries", CreateEntry);
        manual.MapPost("/inputs/{inputId:long}/map", MapInput);
    }

    private static async Task<ApiResponse<ManualInputsResponseDto>> ListInputs(
        ClaimsPrincipal principal,
        ManualService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        return ApiResponse<ManualInputsResponseDto>.Ok(
            await service.ListInputs(userId, ct));
    }

    private static async Task<ApiResponse<ManualSessionStartResponseDto>> StartSession(
        ManualSessionStartRequest? request,
        ClaimsPrincipal principal,
        ManualService service,
        CancellationToken ct)
    {
        if (request is null)
            throw AppProblem.Validation("Request body is required.");

        var userId = GetUserId(principal);
        return ApiResponse<ManualSessionStartResponseDto>.Ok(
            await service.StartSession(userId, request, ct));
    }

    private static async Task<ApiResponse<ManualSessionStopResponseDto>> StopSession(
        ClaimsPrincipal principal,
        ManualService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        return ApiResponse<ManualSessionStopResponseDto>.Ok(
            await service.StopSession(userId, ct));
    }

    private static async Task<ApiResponse<ManualMappingSearchResponseDto>> SearchMapping(
        ClaimsPrincipal principal,
        ManualService service,
        string? query,
        CancellationToken ct)
    {
        return ApiResponse<ManualMappingSearchResponseDto>.Ok(
            await service.SearchMapping(query ?? "", ct));
    }

    private static async Task<ApiResponse<ManualEntryCreateResponseDto>> CreateEntry(
        ManualEntryCreateRequest? request,
        ClaimsPrincipal principal,
        ManualService service,
        CancellationToken ct)
    {
        if (request is null)
            throw AppProblem.Validation("Request body is required.");

        var userId = GetUserId(principal);
        return ApiResponse<ManualEntryCreateResponseDto>.Ok(
            await service.CreateEntry(userId, request, ct));
    }

    private static async Task<ApiResponse<ManualMappingResponseDto>> MapInput(
        long inputId,
        ManualMapInputRequest? request,
        ClaimsPrincipal principal,
        ManualService service,
        CancellationToken ct)
    {
        if (request is null)
            throw AppProblem.Validation("Request body is required.");

        var userId = GetUserId(principal);
        return ApiResponse<ManualMappingResponseDto>.Ok(
            await service.MapInput(inputId, userId, request, ct));
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
