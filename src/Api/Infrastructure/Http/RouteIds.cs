using System.Globalization;

namespace Api.Infrastructure.Http;

public static class RouteIds
{
    /// <summary>
    /// A positive database id from the route, parsed like the user id in the token: ASCII digits only, in the
    /// invariant culture, so no sign, whitespace or culture-specific digits slip through.
    /// </summary>
    public static long Parse(string value, string name)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw AppProblemException.Validation($"{name} must be a positive integer.");
        return id;
    }
}
