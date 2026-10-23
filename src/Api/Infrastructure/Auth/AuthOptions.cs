using System.Text;

namespace Api.Infrastructure.Auth;

public class AuthOptions
{
    /// <summary>HS256 needs a key of at least 256 bits.</summary>
    public const int MinimumSecretBytes = 32;

    /// <summary>
    /// Development secrets that are public in this repository. A server outside Development must never sign tokens
    /// with them, or anyone could mint a token for any account.
    /// </summary>
    public static readonly IReadOnlySet<string> PublicDevelopmentSecrets = new HashSet<string>(StringComparer.Ordinal)
    {
        "dev-only-jwt-secret-do-not-use-in-production",
        "preview-only-jwt-secret-not-for-production-use",
    };

    public string JwtSecret { get; set; } = string.Empty;
    public string AccessTokenTtl { get; set; } = "15m";

    public static bool IsLongEnough(string secret) => Encoding.UTF8.GetByteCount(secret) >= MinimumSecretBytes;
}
