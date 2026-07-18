using System.Text.RegularExpressions;

namespace Playdex.Api.Games;

public static partial class GamesDomain
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();

    public static string NormalizeTitle(string title) =>
        string.Join(" ",
            NonAlphanumericRegex().Replace(title.Trim().ToLowerInvariant(), " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
