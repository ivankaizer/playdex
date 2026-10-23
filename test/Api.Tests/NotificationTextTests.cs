using Api.Modules.Notifications;
using Xunit;

namespace Api.Tests;

public sealed class NotificationTextTests
{
    [Theory]
    [InlineData("Hades", "Hades")]
    [InlineData("  Hades  II ", "Hades II")]
    [InlineData("Hades\nDuration: 99h", "Hades Duration: 99h")]
    [InlineData("Hades\r\n\tII", "Hades II")]
    [InlineData("Hades\u0000II", "Hades II")]
    [InlineData("evil‮gnp.exe", "evilgnp.exe")]
    [InlineData("Zero​width", "Zerowidth")]
    [InlineData("Ōkami HD", "Ōkami HD")]
    [InlineData("Family 👨‍👩‍👧 game", "Family 👨‍👩‍👧 game")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void KeepsOneLineOfVisibleText(string? input, string expected) =>
        Assert.Equal(expected, NotificationText.Clean(input, 120));

    [Fact]
    public void CutsLongTextWithAnEllipsis()
    {
        var cleaned = NotificationText.Clean(new string('a', 200), 10);

        Assert.Equal("aaaaaaaaa…", cleaned);
        Assert.Equal(10, cleaned.Length);
    }

    [Fact]
    public void NeverSplitsASurrogatePair()
    {
        // Each 🎮 is two UTF-16 code units; a cut between them would leave half a character.
        var cleaned = NotificationText.Clean("a🎮🎮🎮🎮", 5);

        Assert.Equal("a🎮…", cleaned);
        Assert.False(char.IsHighSurrogate(cleaned[^2]));
    }

    [Fact]
    public void TextAtTheLimitIsKeptWhole() =>
        Assert.Equal("abcde", NotificationText.Clean("abcde", 5));
}
