using static Playdex.Core.Tests.TestPaths;

namespace Playdex.Core.Tests;

public sealed class RollingLogFileTests
{
    [Fact]
    public void RollsOverBySizeAndKeepsTheNewestFiles()
    {
        var path = Path.Combine(NewTempDir(), "desktop.log");
        static string Line(int i) => $"line {i:00} {new string('x', 40)}";
        var lineBytes = System.Text.Encoding.UTF8.GetByteCount(Line(0) + Environment.NewLine);
        using (var log = new RollingLogFile(path, maxBytes: 2 * lineBytes, keptFiles: 2))
        {
            for (var i = 0; i < 20; i++)
            {
                log.WriteLine(Line(i));
            }
        }

        // Two lines a file: lines 18-19 in .1, 16-17 in .2, older ones deleted, and an empty current file.
        var directory = Path.GetDirectoryName(path)!;
        Assert.Equal(
            ["desktop.1.log", "desktop.2.log", "desktop.log"],
            Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(0, new FileInfo(path).Length);
        Assert.Contains("line 19", File.ReadAllText(RollingLogFile.Rolled(path, 1)), StringComparison.Ordinal);
        Assert.Contains("line 17", File.ReadAllText(RollingLogFile.Rolled(path, 2)), StringComparison.Ordinal);
    }

    [Fact]
    public void ALogAlreadyOverTheLimitRollsOnTheFirstLine()
    {
        var path = Path.Combine(NewTempDir(), "run.log");
        File.WriteAllText(path, new string('x', 500));

        using (var log = new RollingLogFile(path, maxBytes: 100, keptFiles: 3))
        {
            log.WriteLine("fresh start");
        }

        Assert.Equal(500, new FileInfo(RollingLogFile.Rolled(path, 1)).Length);
        Assert.StartsWith("fresh start", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void WhileAnotherProcessHasTheLogOpenItKeepsAppending()
    {
        // Each game's launcher appends to the same run log; a log another one has open can't be renamed.
        var path = Path.Combine(NewTempDir(), "run.log");
        // Closed before the asserts: Windows won't open a file for reading while another handle can write to it.
        using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        using (var log = new RollingLogFile(path, maxBytes: 100, keptFiles: 3))
        {
            for (var i = 0; i < 10; i++)
            {
                log.WriteLine($"line {i} {new string('x', 40)}");
            }
        }

        Assert.False(File.Exists(RollingLogFile.Rolled(path, 1)));
        Assert.Equal(10, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void OldDailyLogsAreDeleted()
    {
        var directory = NewTempDir();
        var old = Path.Combine(directory, "run-20260901.log");
        var recent = Path.Combine(directory, "run-20261020.log");
        var other = Path.Combine(directory, "desktop.log");
        foreach (var file in new[] { old, recent, other })
        {
            File.WriteAllText(file, "x");
        }
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddDays(-30));

        Log.DeleteOlderThan(directory, "run-*.log", TimeSpan.FromDays(14));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));
    }
}
