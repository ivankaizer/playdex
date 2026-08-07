using Web.Shared.Common;
using Xunit;

namespace Web.Tests.Utilities;

public sealed class GameHashTests
{
    [Theory]
    [InlineData(1, "5sm2")]
    [InlineData(2, "aUuZ")]
    [InlineData(42, "3H4h3")]
    [InlineData(12345, "hvWUAK")]
    public void EncodeAndDecode_UseTheReactTransformAndAlphabet(long gameId, string hash)
    {
        Assert.Equal(hash, GameHash.EncodeGameId((double)gameId));
        Assert.Equal(gameId, GameHash.DecodeGameHash(hash));
    }

    [Fact]
    public void DecodeGameHash_IsCaseSensitive()
    {
        Assert.Equal(42, GameHash.DecodeGameHash("3H4h3"));
        Assert.Null(GameHash.DecodeGameHash("3h4H3"));
    }

    [Theory]
    [InlineData("42", "3H4h3")]
    [InlineData(" 42 ", "3H4h3")]
    [InlineData("00042", "3H4h3")]
    public void EncodeGameId_AcceptsTrimmedNumericStrings(string value, string expected)
    {
        Assert.Equal(expected, GameHash.EncodeGameId(value));
    }

    [Fact]
    public void EncodeGameId_PreservesAnAlreadyValidHash()
    {
        Assert.Equal("3H4h3", GameHash.EncodeGameId(" 3H4h3 "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-hash")]
    [InlineData("3H4h!")]
    public void EncodeGameId_RejectsEmptyNonpositiveAndInvalidStrings(string? value)
    {
        Assert.Equal("", GameHash.EncodeGameId(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void EncodeGameId_RejectsInvalidNumbers(double value)
    {
        Assert.Equal("", GameHash.EncodeGameId(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("d5")]
    [InlineData("0")]
    [InlineData("3H4h!")]
    public void DecodeGameHash_RejectsInvalidHashes(string? hash)
    {
        Assert.Null(GameHash.DecodeGameHash(hash));
    }

    [Fact]
    public void DecodeGameHash_RejectsIdsAboveJavascriptMaxSafeInteger()
    {
        Assert.Null(GameHash.DecodeGameHash("3CZuzOaO833qH"));
        Assert.Equal(9_007_199_254_740_991, GameHash.DecodeGameHash("3CZuzOaO7XBhK"));
    }
}
