using System.Globalization;
using System.Numerics;

namespace Web.Shared.Common;

public static class GameHash
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const long Multiplier = 1_299_827;
    private const long Increment = 811;
    private const long JavaScriptMaxSafeInteger = 9_007_199_254_740_991;
    private static readonly BigInteger Base = Alphabet.Length;

    public static string EncodeGameId(double gameId)
    {
        if (!double.IsFinite(gameId) || gameId <= 0 || gameId != Math.Truncate(gameId)) return "";

        return EncodeBase(new BigInteger(gameId) * Multiplier + Increment);
    }

    public static string EncodeGameId(string? gameId)
    {
        var trimmed = gameId?.Trim() ?? "";
        if (trimmed.Length == 0) return "";

        if (trimmed.All(character => character is >= '0' and <= '9'))
        {
            return double.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric)
                ? EncodeGameId(numeric)
                : "";
        }

        return DecodeGameHash(trimmed).HasValue ? trimmed : "";
    }

    public static long? DecodeGameHash(string? hash)
    {
        var trimmed = hash?.Trim() ?? "";
        if (trimmed.Length == 0) return null;

        var decoded = DecodeBase(trimmed);
        if (!decoded.HasValue || decoded.Value < Increment) return null;

        var raw = decoded.Value - Increment;
        if (raw % Multiplier != 0) return null;

        var gameId = raw / Multiplier;
        if (gameId <= 0 || gameId > JavaScriptMaxSafeInteger) return null;

        return (long)gameId;
    }

    private static string EncodeBase(BigInteger value)
    {
        if (value == 0) return Alphabet[0].ToString();

        var encoded = "";
        for (var remaining = value; remaining > 0; remaining /= Base)
        {
            encoded = Alphabet[(int)(remaining % Base)] + encoded;
        }

        return encoded;
    }

    private static BigInteger? DecodeBase(string value)
    {
        var decoded = BigInteger.Zero;
        foreach (var character in value)
        {
            var index = Alphabet.IndexOf(character);
            if (index < 0) return null;
            decoded = decoded * Base + index;
        }

        return decoded;
    }
}
