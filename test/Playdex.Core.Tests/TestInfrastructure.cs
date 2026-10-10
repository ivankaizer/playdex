using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Playdex.Core.Tests;

internal static class TestPaths
{
    internal static string NewTempDir()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "Playdex-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}

internal static class TestProcess
{
    internal static string ExecutablePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "cmd.exe");

    internal static IReadOnlyList<string> Arguments { get; } =
        ["/d", "/c", "ping -n 3 127.0.0.1 > nul"];

    internal static Process Start()
    {
        var startInfo = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Child process did not start.");
    }
}

internal sealed record RecordedRequest(string Method, string Uri, string Authorization, string Body);

internal sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody = "") : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method.Method,
            request.RequestUri?.ToString() ?? "",
            request.Headers.Authorization?.ToString() ?? "",
            body));
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>Loggers that append "[level] Category: message" lines to one file, for tests that read the log back.</summary>
internal sealed class TestLogging : IDisposable
{
    private readonly Lock _gate = new();

    private TestLogging(string filePath) => FilePath = filePath;

    public string FilePath { get; }

    public static TestLogging CreateInDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        return new TestLogging(Path.Combine(directory, $"test-{Guid.NewGuid():N}.log"));
    }

    public ILogger<T> CreateLogger<T>() => new FileLogger<T>(this);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
    }

    private sealed class FileLogger<T>(TestLogging owner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.Write($"[{logLevel.ToString().ToLowerInvariant()}] {typeof(T).Name}: {formatter(state, exception)}");
            }
        }
    }
}
