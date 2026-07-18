using Playdex.Api.Games;
using Xunit;

namespace Playdex.Api.Tests;

public sealed class GameConnectionsTests
{
    [Fact]
    public void MergeGameTotalsKeepsEveryConnectedTotalWhenRepresentativeChanges()
    {
        var totals = new Dictionary<long, double>
        {
            [10] = 20,
            [20] = 100,
            [30] = 5,
        };
        var edges = new List<(long GameId, long ConnectedGameId)>
        {
            (10, 20),
            (20, 30),
        };

        var merged = GameConnections.MergeGameTotals(totals, edges);

        var result = Assert.Single(merged);
        Assert.Equal(20, result.Key);
        Assert.Equal(125, result.Value);
    }

    [Fact]
    public void ResolveConnectedIdsTraversesTheWholeComponent()
    {
        var result = GameConnections.ResolveConnectedIds(20,
        [
            (10, 20),
            (20, 30),
            (99, 100),
        ]);

        Assert.Equal([10L, 20, 30], result.Order());
    }
}
