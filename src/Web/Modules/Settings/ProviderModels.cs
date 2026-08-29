namespace Web.Modules.Settings;

public sealed record ProviderStatus(
    long Id,
    string Platform,
    string? AccountId,
    string Status,
    string? ApiKey,
    bool ApiKeyLocked);
public sealed record ProvidersResponse(IReadOnlyList<ProviderStatus> Providers);
public sealed record ProviderUpdateRequest(long ProviderId, string Status);
public sealed record ProviderUpdateResponse(ProviderStatus Provider);
public sealed record ProviderCredentialUpdateRequest(string? ApiKey, string? AccountId);
public sealed record ProviderCredentialUpdateResponse(ProviderStatus Provider);
public sealed record ProviderCreateResponse(ProviderStatus Provider);
public sealed record PlayniteKeyResponse(string ApiKey);
