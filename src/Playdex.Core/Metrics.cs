using System.Diagnostics;
using System.Text.Json;

namespace Playdex.Core;

/// <summary>
/// Budget measurements from docs/launcher/performance.md, appended to metrics.jsonl and the log.
/// tools/measure.ps1 aggregates them.
/// </summary>
public static class Metrics
{
    private static readonly Lock _gate = new();

    public static double MsSince(long timestamp) => Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

    public static void Record(string metric, double ms, string? detail = null)
    {
        Log.Info($"metric {metric} = {ms:F2} ms{(detail is null ? "" : $" ({detail})")}");
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("at", DateTimeOffset.Now);
            json.WriteString("metric", metric);
            json.WriteNumber("ms", Math.Round(ms, 3));
            if (detail is not null)
            {
                json.WriteString("detail", detail);
            }
            json.WriteEndObject();
        }
        lock (_gate)
        {
            try
            {
                using var file = new FileStream(Paths.MetricsFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                buffer.WriteTo(file);
                file.WriteByte((byte)'\n');
            }
            catch (IOException ex)
            {
                Log.Error("metrics write failed", ex);
            }
        }
    }
}

/// <summary>Fixed-size latency sample buffer; percentiles are computed on demand off the input thread.</summary>
public sealed class LatencyStats(string name, int capacity = 8192)
{
    private readonly double[] _samples = new double[capacity];
    private int _count;

    public int Count => _count;

    public void Add(double ms)
    {
        if (_count < _samples.Length)
        {
            _samples[_count++] = ms;
        }
    }

    public string Summary()
    {
        if (_count == 0)
        {
            return $"{name}: no samples";
        }
        var sorted = _samples.AsSpan(0, _count).ToArray();
        Array.Sort(sorted);
        return $"{name}: n={_count} p50={Pct(sorted, 50):F2} p95={Pct(sorted, 95):F2} p99={Pct(sorted, 99):F2} max={sorted[^1]:F2} ms";
    }

    public void Flush()
    {
        if (_count == 0)
        {
            return;
        }
        var sorted = _samples.AsSpan(0, _count).ToArray();
        Array.Sort(sorted);
        Log.Info(Summary());
        Metrics.Record($"{name}.p50", Pct(sorted, 50), $"n={_count}");
        Metrics.Record($"{name}.p95", Pct(sorted, 95), $"n={_count}");
        Metrics.Record($"{name}.p99", Pct(sorted, 99), $"n={_count}");
    }

    private static double Pct(double[] sorted, int p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p / 100.0 * sorted.Length) - 1, 0, sorted.Length - 1)];
}
