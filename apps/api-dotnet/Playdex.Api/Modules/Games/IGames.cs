namespace Playdex.Api.Modules.Games;

public interface IGames
{
    string NormalizeTitle(string title);
    HashSet<long> ResolveConnectedIds(long gameId, List<(long GameId, long ConnectedGameId)> edges);
    Dictionary<long, double> MergeGameTotals(Dictionary<long, double> gameTotals, List<(long GameId, long ConnectedGameId)> edges);
}
