namespace Playdex.Web.Models;

public sealed record LoginRequest(string? Email, string? Password);
public sealed record LoginResponse(long UserId, string Email, bool IsAdmin, string AccessToken, string RefreshToken);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record RefreshResponse(string? AccessToken, string? RefreshToken);
public sealed record MeResponse(long UserId, string Email, bool IsAdmin);
