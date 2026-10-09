using System.Text;

namespace Playdex.Core.Tests;

public sealed class SteamGridTests
{
    [Fact]
    public void ReadShortcutsFindsEachShortcutsAppIdAndExecutable()
    {
        var vdf = Object("shortcuts",
            Object("0", Int("appid", 3_000_000_001), Text("AppName", "Hades"), Text("Exe", @"""C:\Games\Playdex-hades.exe"""), Int("IsHidden", 0)),
            Object("1", Text("AppName", "No app id"), Text("Exe", @"C:\x.exe")),
            Object("2", Int("appid", 42), Text("exe", @"D:\Balatro.exe"), Object("tags", Text("0", "Cards"))));

        var shortcuts = SteamGrid.ReadShortcuts([.. vdf, 8]);

        Assert.Equal([(3_000_000_001u, @"""C:\Games\Playdex-hades.exe"""), (42u, @"D:\Balatro.exe")], shortcuts);
    }

    [Fact]
    public void ReadShortcutsKeepsWhatWasReadBeforeADamagedTail()
    {
        var vdf = Object("shortcuts", Object("0", Int("appid", 7), Text("Exe", @"C:\a.exe")));

        var shortcuts = SteamGrid.ReadShortcuts([.. vdf[..^1], 0x7F, 0x41]);

        Assert.Equal([(7u, @"C:\a.exe")], shortcuts);
    }

    private static byte[] Object(string name, params byte[][] fields) =>
        [0, .. Name(name), .. fields.SelectMany(f => f), 8];

    private static byte[] Text(string name, string value) => [1, .. Name(name), .. Name(value)];

    private static byte[] Int(string name, uint value) => [2, .. Name(name), .. BitConverter.GetBytes(value)];

    private static byte[] Name(string value) => [.. Encoding.UTF8.GetBytes(value), 0];
}
