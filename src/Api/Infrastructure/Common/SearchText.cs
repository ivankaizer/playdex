using Api.Infrastructure.Http;

namespace Api.Infrastructure.Common;

/// <summary>Free-text searches: bounded in length and matched literally inside ILIKE patterns.</summary>
public static class SearchText
{
    /// <summary>Longest search accepted; game titles fit easily, and longer input only costs the database and IGDB.</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// The escape character <see cref="ContainsPattern"/> uses. Pass it to <c>EF.Functions.ILike</c>: without one,
    /// Npgsql emits <c>ESCAPE ''</c> and the escapes would be matched as literal backslashes.
    /// </summary>
    public const string EscapeCharacter = @"\";

    /// <summary>
    /// An ILIKE pattern that finds <paramref name="text"/> anywhere. Its own <c>%</c>, <c>_</c> and <c>\</c> are
    /// escaped with <see cref="EscapeCharacter"/>, so "50%" finds "50%" and not "500".
    /// </summary>
    public static string ContainsPattern(string text) =>
        $"%{text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")}%";

    /// <summary>The trimmed search, or a validation error when it is longer than <see cref="MaxLength"/>.</summary>
    public static string Bounded(string? text, string field = "query")
    {
        var trimmed = text?.Trim() ?? "";
        return trimmed.Length <= MaxLength
            ? trimmed
            : throw AppProblemException.Validation($"{field} must be at most {MaxLength} characters.");
    }

    /// <summary>An optional filter: null when empty, otherwise <see cref="Bounded"/>.</summary>
    public static string? OptionalFilter(string? text, string field)
    {
        var bounded = Bounded(text, field);
        return bounded.Length == 0 ? null : bounded;
    }
}
