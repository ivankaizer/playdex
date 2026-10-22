using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Xunit;

namespace Api.Tests;

public sealed class GameIdTests
{
    [Theory]
    [InlineData(87421)]
    [InlineData(134643)]
    [InlineData(179739)]
    public void AllDigitEncodedIdsDecodeToTheirGame(long gameId)
    {
        var encoded = GameId.Encode(gameId);

        Assert.Matches("^[0-9]+$", encoded);
        Assert.Equal(gameId, GameId.Parse(encoded));
    }

    [Fact]
    public void EveryEncodedIdRoundTrips()
    {
        for (long gameId = 1; gameId <= 250_000; gameId++)
        {
            Assert.Equal(gameId, GameId.Parse(GameId.Encode(gameId)));
        }
    }

    [Fact]
    public void RawIdsThatAreNotEncodedIdsStillParse()
    {
        Assert.Null(GameId.Decode("42"));
        Assert.Equal(42, GameId.Parse("42"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("not-a-hash")]
    public void InvalidIdsAreRejected(string value) =>
        Assert.Throws<AppProblemException>(() => GameId.Parse(value));
}
