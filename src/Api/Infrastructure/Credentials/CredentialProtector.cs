using System.Security.Cryptography;
using System.Text;
using Api.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Playdex.Persistence;

namespace Api.Infrastructure.Credentials;

/// <summary>
/// Encrypts the third-party secrets polling needs back (Steam Web API key, PSN NPSSO, OpenXBL key) with AES-256-GCM,
/// under a key derived from CREDENTIALS_KEY. Stored values look like <c>enc:v1:&lt;base64 nonce|ciphertext|tag&gt;</c>.
/// Playdex app keys are only ever compared, so they are hashed instead (<see cref="PlaydexAppKeys"/>).
/// </summary>
public sealed class CredentialProtector
{
    public const string Prefix = "enc:v1:";

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] _keyInfo = "playdex-integration-credentials-v1"u8.ToArray();

    private readonly byte[] _key;

    public CredentialProtector(IOptions<CredentialOptions> options) =>
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(options.Value.EncryptionKey), 32, info: _keyInfo);

    public static bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>What integrations.api_key stores for a credential of this type.</summary>
    public string? Protect(IntegrationType type, string? value) => type switch
    {
        _ when value is null => null,
        IntegrationType.Api => PlaydexAppKeys.Hash(value),
        _ => Protect(value),
    };

    public string? Protect(string? value)
    {
        if (value is null || IsProtected(value))
        {
            return value;
        }

        var plaintext = Encoding.UTF8.GetBytes(value);
        var payload = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = payload.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, payload.AsSpan(NonceSize, plaintext.Length), payload.AsSpan(NonceSize + plaintext.Length, TagSize));
        return Prefix + Convert.ToBase64String(payload);
    }

    /// <summary>Returns a value stored before encryption existed unchanged, until <see cref="CredentialEncryptionBackfill"/> has run.</summary>
    /// <exception cref="CryptographicException">The value was encrypted with another key, or changed.</exception>
    public string? Unprotect(string? value)
    {
        if (value is null || !IsProtected(value))
        {
            return value;
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(value[Prefix.Length..]);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("Encrypted credential is not valid base64.", exception);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Encrypted credential is truncated.");
        }

        var plaintext = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            payload.AsSpan(0, NonceSize),
            payload.AsSpan(NonceSize, plaintext.Length),
            payload.AsSpan(NonceSize + plaintext.Length, TagSize),
            plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
