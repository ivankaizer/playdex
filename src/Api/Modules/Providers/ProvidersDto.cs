namespace Api.Modules.Providers;

public sealed record ProviderPlatformDto(string Key, string Name);
public sealed record ProviderStatusDto(
    long Id,
    string Platform,
    string? AccountId,
    string Status,
    bool HasApiKey,
    bool ApiKeyLocked,
    ProviderPlatformDto? SessionPlatform = null,
    IReadOnlyList<ProviderPlatformDto>? SessionPlatformOptions = null,
    string? PausedReason = null,
    DateTimeOffset? PausedAt = null);
public sealed record ProvidersResponseDto(IReadOnlyList<ProviderStatusDto> Providers);
public sealed record ProviderUpdateRequest(long ProviderId, string Status);
public sealed record ProviderUpdateResponseDto(ProviderStatusDto Provider);
public sealed record CreateProviderRequest(string? Platform, string? ApiKey, string? AccountId);
public sealed record CreateProviderResponseDto(ProviderStatusDto Provider);
public sealed record ApiKeyResponseDto(string ApiKey);
public sealed record ProviderCredentialUpdateRequest(
    string? ApiKey,
    string? AccountId);
public sealed record ProviderCredentialChanges(
    bool ApiKeyChanged,
    string? ApiKey,
    bool AccountIdChanged,
    string? AccountId);
public sealed record ProviderCredentialUpdateResponseDto(ProviderStatusDto Provider);
public sealed record ProviderSessionPlatformUpdateRequest(
    string? Platform,
    bool RewriteExistingSessions);
public sealed record ProviderSessionPlatformUpdateResponseDto(ProviderStatusDto Provider);
/// <param name="ProviderId">
/// The Xbox integration to connect. Without it, the user's oldest Xbox integration, or a new one when there is none.
/// </param>
public sealed record OpenXblConnectRequest(string? ApiKey, string? Xuid, long? ProviderId = null);
public sealed record OpenXblConnectResponseDto(ProviderStatusDto Provider);
internal sealed record ValidatedProviderCreation(
    string Platform,
    Playdex.Persistence.IntegrationType Type,
    string ApiKey,
    string? AccountId);
public sealed record ProviderCreateResult(ProviderStatusDto? Provider, bool LimitReached, bool DuplicateAccount = false);
public sealed record OpenXblConnectResult(
    ProviderStatusDto? Provider,
    bool ProviderMissing = false,
    bool ApiKeyLocked = false);
public sealed record ProviderSessionPlatformUpdateResult(
    ProviderStatusDto? Provider,
    bool InvalidPlatform,
    bool UnsupportedIntegration = false);
