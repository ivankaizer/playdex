using Xunit;

namespace Playdex.ContractTests;

/// <summary>
/// Contract-verification tests for the root health-check endpoint and
/// unauthenticated access to protected endpoints.
///
/// Skips when <c>PLAYDEX_CONTRACT_DATABASE_URL</c> is not configured.
/// </summary>
public sealed class RootContractTests : ContractTestBase
{
    public RootContractTests(ContractFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task GetApi_ReturnsOk_AcrossBothImplementations()
    {
        if (!Fixture.IsAvailable)
            return;

        await AssertEquivalentEndpoints(
            client => client.GetAsync("/api"),
            "GET /api");
    }

    [Fact]
    public async Task GetApiMe_WithoutToken_Returns401_AcrossBothImplementations()
    {
        if (!Fixture.IsAvailable)
            return;

        await AssertEquivalentEndpoints(
            client => client.GetAsync("/api/me"),
            "GET /api/me (unauthenticated)");
    }
}
