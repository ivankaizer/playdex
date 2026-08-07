using System.Globalization;
using Api.Shared.Http;

namespace Api.Modules.Accounts;

internal readonly record struct AccountDateRange(DateTimeOffset? From, DateTimeOffset? ToExclusive)
{
    private const string InvalidDatesMessage = "from and to must be valid dates.";
    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm'Z'",
        "yyyy-MM-dd'T'HH:mmzzz",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    public static AccountDateRange ParseOptional(string? from, string? to) => Parse(from, to, required: false);

    public static AccountDateRange ParseRequired(string? from, string? to) => Parse(from, to, required: true);

    private static AccountDateRange Parse(string? from, string? to, bool required)
    {
        if (required && (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)))
            throw AppProblem.Validation("from and to are required.");

        var parsedFrom = ParseBound(from);
        var parsedTo = ParseBound(to);

        if (parsedFrom.HasValue && parsedTo.HasValue && parsedFrom > parsedTo)
            throw AppProblem.Validation("from must be before to.");

        if (parsedTo.HasValue && parsedTo.Value.UtcDateTime.Date == DateTime.MaxValue.Date)
            throw AppProblem.Validation(InvalidDatesMessage);

        var toExclusive = parsedTo.HasValue
            ? new DateTimeOffset(parsedTo.Value.UtcDateTime.Date, TimeSpan.Zero).AddDays(1)
            : (DateTimeOffset?)null;

        return new AccountDateRange(parsedFrom, toExclusive);
    }

    private static DateTimeOffset? ParseBound(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (!DateTimeOffset.TryParseExact(
                value,
                IsoFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw AppProblem.Validation(InvalidDatesMessage);
        }

        return parsed;
    }
}
