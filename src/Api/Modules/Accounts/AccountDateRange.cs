using System.Globalization;
using Api.Infrastructure.Common;
using Api.Infrastructure.Http;

namespace Api.Modules.Accounts;

/// <summary>
/// A <c>from</c>/<c>to</c> query range. A date-only bound is a calendar day in the client's time zone; <c>to</c> is
/// inclusive, so the range ends where the day after it begins.
/// </summary>
internal readonly record struct AccountDateRange(DateTimeOffset? From, DateTimeOffset? ToExclusive)
{
    private const string InvalidDatesMessage = "from and to must be valid dates.";
    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm'Z'",
        "yyyy-MM-dd'T'HH:mmzzz",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    public static AccountDateRange ParseOptional(string? from, string? to, TimeZoneInfo zone) =>
        Parse(from, to, required: false, zone);

    public static AccountDateRange ParseRequired(string? from, string? to, TimeZoneInfo zone) =>
        Parse(from, to, required: true, zone);

    private static AccountDateRange Parse(string? from, string? to, bool required, TimeZoneInfo zone)
    {
        if (required && (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)))
        {
            throw AppProblemException.Validation("from and to are required.");
        }

        var parsedFrom = ParseBound(from, zone);
        var parsedTo = ParseBound(to, zone);

        if (parsedFrom.HasValue && parsedTo.HasValue && parsedFrom > parsedTo)
        {
            throw AppProblemException.Validation("from must be before to.");
        }

        try
        {
            var toExclusive = parsedTo.HasValue
                ? LocalCalendar.StartOfNextDay(parsedTo.Value, zone)
                : (DateTimeOffset?)null;
            return new AccountDateRange(parsedFrom, toExclusive);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw AppProblemException.Validation(InvalidDatesMessage);
        }
    }

    private static DateTimeOffset? ParseBound(string? value, TimeZoneInfo zone)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                return LocalCalendar.StartOf(day, zone);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            throw AppProblemException.Validation(InvalidDatesMessage);
        }

        if (!DateTimeOffset.TryParseExact(
                value,
                IsoFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw AppProblemException.Validation(InvalidDatesMessage);
        }

        return parsed;
    }
}
