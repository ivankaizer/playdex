namespace Playdex.Api.Modules.Providers;

public sealed record ProviderStatusDto(long Id, string Platform, string? AccountId, string Status, string? ApiKey);
public sealed record ProvidersResponseDto(IReadOnlyList<ProviderStatusDto> Providers);
public sealed record ProviderUpdateRequest(long ProviderId, string Status);
public sealed record ProviderUpdateResponseDto(ProviderStatusDto Provider);
public sealed record PlayniteKeyResponseDto(string ApiKey);
