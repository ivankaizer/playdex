using System.Text;

namespace Api.Infrastructure.Credentials;

public class CredentialOptions
{
    /// <summary>The AES-256 key is derived from it, so it needs at least 256 bits.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// Development keys that are public in this repository. Outside Development they would let anyone with a database
    /// dump decrypt every stored credential.
    /// </summary>
    public static readonly IReadOnlySet<string> PublicDevelopmentKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "dev-only-credentials-key-do-not-use-in-production",
        "preview-only-credentials-key-not-for-production-use",
    };

    public string EncryptionKey { get; set; } = string.Empty;

    public static bool IsLongEnough(string key) => Encoding.UTF8.GetByteCount(key) >= MinimumKeyBytes;
}
