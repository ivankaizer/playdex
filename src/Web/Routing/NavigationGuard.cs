using Web.Shared.Common;

namespace Web.Routing;

public enum NavigationOutcome
{
    Render,
    Wait,
    Redirect,
    HomeFallback,
}

public sealed record NavigationDecision(
    NavigationOutcome Outcome,
    string? RedirectPath = null,
    string? GameHash = null);

public enum NavigationInstructionKind
{
    NoOp,
    Navigate,
}

public sealed record NavigationInstruction(
    NavigationInstructionKind Kind,
    string? Target = null,
    bool ReplaceHistory = false,
    bool ScrollToTop = false);

public static class NavigationGuard
{
    private const string GamesPrefix = "/games/";

    private static readonly HashSet<string> ProtectedPaths = new(StringComparer.Ordinal)
    {
        "/dashboard",
        "/sessions",
        "/recap",
        "/mapping",
        "/settings",
        "/admin",
    };

    public static NavigationDecision Resolve(
        string path,
        bool isLoggedIn,
        bool isProfileResolved,
        bool isAdmin)
    {
        if (path == "/") return new(NavigationOutcome.Render);

        var isGamePath = path.StartsWith(GamesPrefix, StringComparison.Ordinal);
        if (!isGamePath && !ProtectedPaths.Contains(path))
        {
            return new(NavigationOutcome.HomeFallback);
        }

        if (!isLoggedIn) return new(NavigationOutcome.Redirect, "/");

        if (path == "/admin")
        {
            if (!isProfileResolved) return new(NavigationOutcome.Wait);
            return isAdmin
                ? new(NavigationOutcome.Render)
                : new(NavigationOutcome.Redirect, "/dashboard");
        }

        return isGamePath
            ? new(NavigationOutcome.Render, GameHash: NormalizeGameHash(path[GamesPrefix.Length..]))
            : new(NavigationOutcome.Render);
    }

    public static NavigationInstruction PlanNavigation(
        string currentPathAndQuery,
        string target,
        bool replaceHistory)
    {
        if (!replaceHistory && string.Equals(currentPathAndQuery, target, StringComparison.Ordinal))
        {
            return new(NavigationInstructionKind.NoOp);
        }

        return new(
            NavigationInstructionKind.Navigate,
            target,
            ReplaceHistory: replaceHistory,
            ScrollToTop: !replaceHistory);
    }

    public static string? NormalizeGameHash(string escapedRemainder)
    {
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(escapedRemainder);
        }
        catch (UriFormatException)
        {
            return null;
        }

        var hash = decoded.TrimEnd('/').Trim();
        if (hash.All(character => character is >= '0' and <= '9'))
        {
            hash = GameHash.EncodeGameId(hash);
        }

        return GameHash.DecodeGameHash(hash).HasValue ? hash : null;
    }
}
