namespace Api.Tests;

using Xunit;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresFixtureTests(PostgresFixture postgres)
{
    [Fact]
    public void FixtureIsAvailableWhenIntegrationDatabaseIsConfigured()
    {
        var configured = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL"));

        Assert.Equal(configured, postgres.IsConfigured);
    }
}
