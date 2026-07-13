using System.Globalization;

namespace Playdex.Web.Utilities;

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
        var value = decimals == 0
            ? Math.Floor(hours + 0.5)
            : double.IsFinite(hours)
                ? Math.Round(hours, decimals, MidpointRounding.AwayFromZero)
                : 0;

        if (value == 0) value = 0;
        var pattern = decimals == 0 ? "0" : $"0.{new string('#', decimals)}";
        return $"{value.ToString(pattern, EnUs)}h";
    }

    public static string FormatUtcDateInput(DateTimeOffset date) =>
        date.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Whole(double value) => value.ToString("0", EnUs);
}
