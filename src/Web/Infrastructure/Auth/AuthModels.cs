namespace Web.Infrastructure.Auth;

public sealed record LoginRequest(string? Email, string? Password);
public sealed record LoginResponse(long UserId, string Email, string AccessToken, string RefreshToken);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record RefreshResponse(string AccessToken, string RefreshToken);
public sealed record MeResponse(long UserId, string Email);
