using Playdex.Api.Modules.Recap;
using Xunit;

namespace Playdex.Api.Tests;

public sealed class RecapRangeTests
{
    [Fact]
    public void EndpointExclusiveTo_IsNotAdvancedASecondDay()
    {
        var endpointExclusiveTo = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(endpointExclusiveTo, RecapRange.ResolveEndExclusive(
            new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
            endpointExclusiveTo));
    }

    [Fact]
    public void AllTimeRange_IncludesTheCurrentUtcDay()
    {
        var now = new DateTimeOffset(2026, 7, 17, 23, 59, 0, TimeSpan.Zero);

        Assert.Equal(
            new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero),
            RecapRange.ResolveEndExclusive(now, null));
    }
}
