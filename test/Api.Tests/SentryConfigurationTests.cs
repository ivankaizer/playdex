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

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("__SENTRY_RELEASE__", null)]
    [InlineData(" playdex@1.2.3 ", "playdex@1.2.3")]
    [InlineData("__half-placeholder", "__half-placeholder")]
    public void SettingsDropBlanksAndEntrypointPlaceholders(string? value, string? expected) =>
        Assert.Equal(expected, Microsoft.Extensions.Hosting.Extensions.NormalizeSetting(value));

    [Fact]
    public void ApiSentrySettingsFallBackPastPlaceholders()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SENTRY_DSN"] = "https://public@example.ingest.sentry.io/42",
            ["SENTRY_WEB_DSN"] = "__SENTRY_WEB_DSN__",
            ["SENTRY_ENVIRONMENT"] = "",
            ["Sentry:Environment"] = "__SENTRY_ENVIRONMENT__",
            ["SENTRY_RELEASE"] = " playdex@1.2.3 ",
        });

        var settings = Api.Observability.PlaydexSentrySettings.From(
            configuration,
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Staging" });

        Assert.Equal("https://public@example.ingest.sentry.io/42", settings.WebDsn);
        Assert.Equal("Staging", settings.Environment);
        Assert.Equal("playdex@1.2.3", settings.Release);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
