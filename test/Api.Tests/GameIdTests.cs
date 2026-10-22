using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Xunit;

namespace Api.Tests;

public sealed class GameIdTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(811L)]
    [InlineData(1_299_827L)]
    [InlineData(123_456_789L)]
    [InlineData(1_000_000_000_000L)]
    public void EncodedIdsRoundTrip(long id)
    {
        var encoded = GameId.Encode(id);

        Assert.Equal(id, GameId.Decode(encoded));
        Assert.Equal(id, GameId.Parse(encoded));
    }

    [Theory]
    [InlineData(87_421L, "2029282")]
    [InlineData(134_643L)]
    [InlineData(179_739L)]
    public void EncodingsMadeOfDigitsOnlyStillParseAsTheEncodedGame(long id, string? expectedEncoding = null)
    {
        var encoded = GameId.Encode(id);
        Assert.True(encoded.All(char.IsAsciiDigit), $"{id} encodes as {encoded}");
        if (expectedEncoding is not null) Assert.Equal(expectedEncoding, encoded);

        Assert.Equal(id, GameId.Parse(encoded));
    }

    [Fact]
    public void EveryIdInALargeRangeRoundTripsThroughParse()
    {
        for (long id = 1; id <= 250_000; id++)
        {
            var encoded = GameId.Encode(id);
            if (GameId.Parse(encoded) != id) Assert.Fail($"{id} encodes as {encoded} but parses as {GameId.Parse(encoded)}");
        }
    }

    [Theory]
    [InlineData("123", 123L)]
    [InlineData("42", 42L)]
    public void RawNumericIdsThatAreNotEncodingsAreStillAccepted(string value, long expected)
    {
        Assert.Null(GameId.Decode(value));
        Assert.Equal(expected, GameId.Parse(value));
    }

    [Fact]
    public void DecodeIgnoresSurroundingWhitespace()
    {
        var encoded = GameId.Encode(5150);

        Assert.Equal(5150, GameId.Decode($"  {encoded}\t"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("not-an-id")]
    [InlineData("abc!")]
    [InlineData("ÿ")]
    public void InvalidIdsAreValidationProblemsNamingTheField(string value)
    {
        var error = Assert.Throws<AppProblemException>(() => GameId.Parse(value, "connectedGameId"));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("connectedGameId must be a valid game id.", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]          // decodes below the increment
    [InlineData("zz")]         // a valid alphabet string that is not a multiple of the multiplier
    [InlineData("hello world")]
    public void DecodeRejectsStringsThatAreNotEncodings(string value) => Assert.Null(GameId.Decode(value));

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void OnlyPositiveIdsCanBeEncoded(long id) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GameId.Encode(id));
}
