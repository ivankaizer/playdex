namespace Playdex.Api.Tests;

using Xunit;

[Collection(PostgresCollection.Name)]
public sealed class PostgresFixtureTests(PostgresFixture postgres)
{
    [Fact]
    public void Fixture_IsAvailableWhenIntegrationDatabaseIsConfigured()
    {
        var configured = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL"));

        Assert.Equal(configured, postgres.IsConfigured);
    }
}
