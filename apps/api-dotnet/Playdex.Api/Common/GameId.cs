using System.Globalization;
using Playdex.Api.Http;

namespace Playdex.Api.Common;

public static class GameId
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const long Multiplier = 1299827;
    private const long Increment = 811;

    public static string Encode(long gameId)
    {
        if (gameId <= 0)
        {
            return "";
        }

        var value = (ulong)(gameId * Multiplier + Increment);
        if (value == 0)
        {
            return Alphabet[0].ToString();
        }

        var chars = new char[12];
        var index = 11;
        while (value > 0)
        {
            chars[index--] = Alphabet[(int)(value % 62)];
            value /= 62;
        }

        return new string(chars, index + 1, 11 - index);
    }

    public static long? Decode(string hash)
    {
        var trimmed = hash.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        ulong decoded = 0;
        foreach (var c in trimmed)
        {
            var idx = Alphabet.IndexOf(c);
            if (idx < 0)
            {
                return null;
            }
            decoded = decoded * 62 + (ulong)idx;
        }

        if (decoded < (ulong)Increment)
        {
            return null;
        }

        var raw = decoded - (ulong)Increment;
        if (raw % (ulong)Multiplier != 0)
        {
            return null;
        }

        var gameId = (long)(raw / (ulong)Multiplier);
        if (gameId <= 0)
        {
            return null;
        }

        return gameId;
    }

    public static long Parse(string value, string field = "gameId")
    {
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        var decoded = Decode(value);
        if (decoded.HasValue)
        {
            return decoded.Value;
        }

        throw AppProblem.Validation($"{field} must be a valid game id.");
    }
}
