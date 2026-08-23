using System.Diagnostics;
using System.Net;
using System.Text;
using Wrapper;

namespace Wrapper.Tests;

internal static class TestPaths
{
    internal static string NewTempDir()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            AppBrand.StorageDirectoryName + "-tests",
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
