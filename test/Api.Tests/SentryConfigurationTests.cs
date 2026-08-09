using Microsoft.Extensions.Configuration;
using Xunit;

namespace Api.Tests;

public sealed class SentryConfigurationTests
{
    [Fact]
    public void ConventionalDsnOverridesEmptyAppSettingsValue()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Sentry:Dsn"] = "",
            ["SENTRY_DSN"] = "https://public@example.ingest.sentry.io/42",
        });

        Assert.Equal(
            "https://public@example.ingest.sentry.io/42",
            Microsoft.Extensions.Hosting.Extensions.GetSentryDsn(configuration));
    }

    [Fact]
    public void ConventionalTraceRateOverridesAppSettingsValue()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Sentry:TracesSampleRate"] = "0.2",
            ["SENTRY_TRACES_SAMPLE_RATE"] = "0.75",
        });

        Assert.Equal(
            0.75,
            Microsoft.Extensions.Hosting.Extensions.GetTraceSampleRate(configuration));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
