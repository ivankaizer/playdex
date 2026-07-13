namespace Playdex.Web.Models;

public sealed record ProviderStatus(long Id, string Platform, string? AccountId, string Status, string? ApiKey);
public sealed record ProvidersResponse(IReadOnlyList<ProviderStatus> Providers);
public sealed record ProviderUpdateRequest(long ProviderId, string Status);
public sealed record ProviderUpdateResponse(ProviderStatus Provider);
public sealed record PlayniteKeyResponse(string ApiKey);
