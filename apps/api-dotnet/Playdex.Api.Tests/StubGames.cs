using Playdex.Api.Modules.Games;

namespace Playdex.Api.Tests;

public sealed class StubGames : IGames
{
    public string NormalizeTitle(string title) => title.Trim().ToLowerInvariant();

    public HashSet<long> ResolveConnectedIds(long gameId, List<(long GameId, long ConnectedGameId)> edges)
        => [gameId];

    public Dictionary<long, double> MergeGameTotals(
        Dictionary<long, double> gameTotals,
        List<(long GameId, long ConnectedGameId)> edges)
        => gameTotals;
}
