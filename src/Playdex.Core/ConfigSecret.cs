using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Core;

/// <summary>
/// The keys in config.json (the Playdex API key, the SteamGridDB key) at rest: encrypted with DPAPI for the current
/// Windows user, so another account on the PC, or a copy of the file, doesn't get them. Saved as "dpapi:" and base64.
/// A value without the prefix is a key saved before keys were encrypted; it is read as it is.
/// </summary>
internal static unsafe class ConfigSecret
{
    public const string Prefix = "dpapi:";

    public static bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static string Protect(string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        try
        {
            fixed (byte* data = plain)
            {
                var input = new Native.DataBlob { Size = (uint)plain.Length, Data = data };
                Native.DataBlob output;
                if (!Native.CryptProtectData(&input, 0, null, 0, 0, Native.CryptProtectUiForbidden, &output))
                {
                    throw new CryptographicException($"DPAPI could not encrypt a key (error {Marshal.GetLastPInvokeError()}).");
                }
                try
                {
                    return Prefix + Convert.ToBase64String(new ReadOnlySpan<byte>(output.Data, (int)output.Size));
                }
                finally
                {
                    Native.LocalFree((nint)output.Data);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>
    /// The key in <paramref name="value"/> (a <see cref="Protect"/> result), or null when this Windows user can't decrypt
    /// it: another user's config, one copied from another PC, or a damaged value.
    /// </summary>
    public static string? Unprotect(string value)
    {
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(value[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (blob.Length == 0)
        {
            return null;
        }

        fixed (byte* data = blob)
        {
            var input = new Native.DataBlob { Size = (uint)blob.Length, Data = data };
            Native.DataBlob output;
            if (!Native.CryptUnprotectData(&input, 0, null, 0, 0, Native.CryptProtectUiForbidden, &output))
            {
                return null;
            }
            var plain = new Span<byte>(output.Data, (int)output.Size);
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
                Native.LocalFree((nint)output.Data);
            }
        }
    }
}

/// <summary>
/// A config key in config.json: encrypted when written (<see cref="ConfigSecret"/>), decrypted when read. A key this
/// Windows user can't decrypt reads as none, so the app asks for it again instead of failing to start.
/// </summary>
internal sealed class ConfigSecretJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? "";
        if (!ConfigSecret.IsProtected(value))
        {
            return value;
        }
        if (ConfigSecret.Unprotect(value) is { } key)
        {
            return key;
        }
        Log.Info("config: a saved key can't be decrypted by this Windows user (another user's or another PC's config); enter it again in Settings");
        return "";
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(string.IsNullOrEmpty(value) ? "" : ConfigSecret.Protect(value));
}
