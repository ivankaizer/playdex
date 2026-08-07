using Microsoft.Extensions.Logging;
using static GameSessionWrapper.Tests.TestPaths;

namespace GameSessionWrapper.Tests;

public sealed class LoggingTests
{
    [Fact]
    public Task TestTypedLoggerWritesStructuredFileEntry()
    {
        var directory = NewTempDir();
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        var logger = logging.CreateLogger<LoggingTestsMarker>();

        logger.Log(
            LogLevel.Information,
            new EventId(42, "test-event"),
            "logging migration works",
            exception: null,
            static (state, _) => state);

        var contents = File.ReadAllText(logging.FilePath);
        Assert.True(contents.Contains("[information]", StringComparison.Ordinal));
        Assert.True(contents.Contains(nameof(LoggingTestsMarker), StringComparison.Ordinal));
        Assert.True(contents.Contains("logging migration works", StringComparison.Ordinal));
        Assert.True(typeof(AppLoggerFactory).Assembly.GetType("GameSessionWrapper.FileLogger") is null);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestLoggerCanSwitchToConfiguredDirectory()
    {
        var startupDirectory = NewTempDir();
        var configuredDirectory = NewTempDir();
        using var logging = AppLoggerFactory.CreateInDirectory(startupDirectory);
        var logger = logging.CreateLogger<LoggingTestsMarker>();

        logging.UseDirectory(configuredDirectory);
        logger.Log(
            LogLevel.Information,
            new EventId(43, "configured-directory"),
            "configured logging works",
            exception: null,
            static (state, _) => state);

        Assert.Equal(configuredDirectory, Path.GetDirectoryName(logging.FilePath));
        Assert.True(File.ReadAllText(logging.FilePath).Contains("configured logging works", StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    private sealed class LoggingTestsMarker;
}
