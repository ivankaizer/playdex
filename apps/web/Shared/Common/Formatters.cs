using System.Globalization;
using System.Numerics;

namespace Playdex.Web.Shared.Common;

public enum DurationFormat
{
    Short,
    Long,
}

public static class Formatters
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    public static string FormatPlatformLabel(string platform) => platform switch
    {
        "ps5" or "psn" => "PS5",
        "xbox-series-xs" => "Xbox Series X|S",
        "pc" => "PC",
        "switch-1" => "Nintendo Switch 1",
        "switch-2" => "Nintendo Switch 2",
        "steam" => "Steam",
        "playnite" => "Playnite",
        "manual" => "Manual",
        "unknown" => "Unknown",
        _ => platform,
    };

    public static string FormatProviderName(string platform) => platform switch
    {
        "steam" => "Steam",
        "psn" => "PlayStation",
        "playnite" => "Playnite",
        "manual" => "Manual",
        _ => platform,
    };

    public static string FormatSessionPlatformLabel(string platform, string? mappedPlatform) =>
        FormatPlatformLabel(mappedPlatform ?? platform);

    public static string FormatStatsPlatformLabel(string platform) => FormatPlatformLabel(platform);

    public static string FormatManualMappingType(string? source) => source == "igdb" ? "IGDB" : "Local";

    public static string FormatManualInputLabel(
        string input,
        string? mappedGameTitle,
        string? mappedGamePlatform,
        string? mappedSource)
    {
        if (mappedGameTitle is null) return input;

        var platform = string.IsNullOrEmpty(mappedGamePlatform)
            ? "Unknown platform"
            : FormatPlatformLabel(mappedGamePlatform);
        return $"{platform} · {FormatManualMappingType(mappedSource)} · {mappedGameTitle}";
    }

    public static string FormatEntryResultLabel(string source, string title, string? platform)
    {
        if (source != "local") return title;

        var platformLabel = string.IsNullOrEmpty(platform)
            ? "Unknown platform"
            : FormatPlatformLabel(platform);
        return $"{title} · {platformLabel}";
    }

    public static string FormatDateLabel(DateTimeOffset date) =>
        date.ToLocalTime().ToString("MMM d, yyyy", EnUs);

    public static string FormatTimeLabel(DateTimeOffset date) =>
        date.ToLocalTime().ToString("h:mm tt", EnUs);

    public static string FormatLocalDateTime(DateTimeOffset date) =>
        date.ToLocalTime().ToString("M/d/yyyy, h:mm:ss tt", EnUs);

    public static string FormatDuration(double seconds, DurationFormat format)
    {
        var safeSeconds = Math.Max(0, Math.Floor(seconds));
        var hours = Math.Floor(safeSeconds / 3600);
        var minutes = Math.Floor(safeSeconds % 3600 / 60);

        if (hours <= 0 && minutes <= 0)
        {
            return format == DurationFormat.Short ? "0m" : "0 min";
        }

        if (format == DurationFormat.Short)
        {
            return hours > 0
                ? $"{Whole(hours)}h {Whole(minutes).PadLeft(2, '0')}m"
                : $"{Whole(minutes)}m";
        }

        return hours > 0
            ? $"{Whole(hours)}h {Whole(minutes)}m"
            : $"{Whole(minutes)} min";
    }

    public static string FormatHours(double seconds, int decimals)
    {
        var hours = seconds / 3600;
        if (decimals != 0) return $"{FormatJavaScriptFixedNumber(hours, decimals)}h";

        var value = Math.Floor(hours + 0.5);
        if (value == 0) value = 0;
        return $"{value.ToString("0", EnUs)}h";
    }

    internal static string FormatJavaScriptFixedNumber(double value, int decimals)
    {
        if (!double.IsFinite(value)) return "0";
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        if (decimals > 100) throw new ArgumentOutOfRangeException(nameof(decimals));
        if (value == 0) return "0";

        var isNegative = value < 0;
        var magnitude = Math.Abs(value);
        if (magnitude >= 1e21) return FormatJavaScriptNumber(value);

        var bits = (ulong)BitConverter.DoubleToInt64Bits(magnitude);
        var exponentBits = (int)(bits >> 52 & 0x7ff);
        var fractionBits = bits & 0x000f_ffff_ffff_ffff;
        var significand = exponentBits == 0
            ? new BigInteger(fractionBits)
            : new BigInteger(fractionBits | 1UL << 52);
        var binaryExponent = exponentBits == 0
            ? -1074
            : exponentBits - 1023 - 52;

        var numerator = significand * BigInteger.Pow(5, decimals);
        var scaledExponent = binaryExponent + decimals;
        BigInteger rounded;
        if (scaledExponent >= 0)
        {
            rounded = numerator << scaledExponent;
        }
        else
        {
            var denominator = BigInteger.One << -scaledExponent;
            rounded = BigInteger.DivRem(numerator, denominator, out var remainder);
            if (remainder * 2 >= denominator) rounded++;
        }

        if (rounded.IsZero) return "0";
        var fixedNumber = double.Parse(
            $"{(isNegative ? "-" : string.Empty)}{rounded.ToString(CultureInfo.InvariantCulture)}e-{decimals}",
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
        return FormatJavaScriptNumber(fixedNumber);
    }

    public static string FormatUtcDateInput(DateTimeOffset date) =>
        date.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatJavaScriptNumber(double value)
    {
        if (value == 0) return "0";

        var isNegative = value < 0;
        var roundTrip = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentMarker = roundTrip.IndexOf('E');
        var explicitExponent = exponentMarker >= 0
            ? int.Parse(roundTrip[(exponentMarker + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture)
            : 0;
        var mantissa = exponentMarker >= 0 ? roundTrip[..exponentMarker] : roundTrip;
        var decimalPoint = mantissa.IndexOf('.');
        var integerDigits = decimalPoint >= 0 ? decimalPoint : mantissa.Length;
        var digits = mantissa.Replace(".", string.Empty, StringComparison.Ordinal);
        var leadingZeros = digits.Length - digits.TrimStart('0').Length;
        digits = digits.TrimStart('0').TrimEnd('0');
        var exponent = explicitExponent + integerDigits - 1 - leadingZeros;

        string formatted;
        if (exponent is >= -6 and < 21)
        {
            var decimalIndex = exponent + 1;
            formatted = decimalIndex <= 0
                ? $"0.{new string('0', -decimalIndex)}{digits}"
                : decimalIndex >= digits.Length
                    ? $"{digits}{new string('0', decimalIndex - digits.Length)}"
                    : $"{digits[..decimalIndex]}.{digits[decimalIndex..]}";
        }
        else
        {
            var mantissaText = digits.Length == 1 ? digits : $"{digits[0]}.{digits[1..]}";
            formatted = $"{mantissaText}e{(exponent >= 0 ? "+" : string.Empty)}{exponent}";
        }

        return isNegative ? $"-{formatted}" : formatted;
    }

    private static string Whole(double value) => value.ToString("0", EnUs);
}
