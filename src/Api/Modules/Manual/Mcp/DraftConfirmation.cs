using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Api.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace Api.Modules.Manual.Mcp;

/// <summary>
/// The token that lets the draft card change and accept one batch. <c>draft_sessions</c> returns it in the result's
/// <c>_meta</c>, which hosts hand to the card but not to the model, so a model that calls an app-only tool directly
/// (in a host that ignores <c>visibility</c>) cannot accept drafts without the user. It is an HMAC of the user and the
/// batch, so it needs no storage and stays valid for the batch's life.
/// </summary>
public sealed class DraftConfirmation(IOptions<AuthOptions> auth)
{
    private readonly byte[] _key = HKDF.DeriveKey(HashAlgorithmName.SHA256,
        Encoding.UTF8.GetBytes(auth.Value.JwtSecret), 32, info: "playdex:mcp-draft-confirmation"u8.ToArray());

    public string Issue(long userId, Guid batchId) => Base64Url.EncodeToString(Sign(userId, batchId));

    public bool IsValid(long userId, Guid batchId, string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 64)
        {
            return false;
        }

        byte[] given;
        try { given = Base64Url.DecodeFromChars(token); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(given, Sign(userId, batchId));
    }

    private byte[] Sign(long userId, Guid batchId) => HMACSHA256.HashData(_key,
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{userId}:{batchId:D}")));
}
