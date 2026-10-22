using Api.Infrastructure.Http;
using Api.Modules.ApiCapture;
using Xunit;

namespace Api.Tests;

public sealed class ApiCaptureValidationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(86_400)]
    public void RecordedSessionsUpToADayAreAccepted(int seconds) =>
        Assert.Equal(seconds, ApiCaptureEndpoints.ValidateDuration(seconds));

    [Theory]
    [InlineData(null, "durationInSeconds must be a positive number.")]
    [InlineData(0, "durationInSeconds must be a positive number.")]
    [InlineData(86_401, "durationInSeconds must be at most 86400 (24 hours).")]
    [InlineData(2_000_000_000, "durationInSeconds must be at most 86400 (24 hours).")]
    public void RecordedSessionsLongerThanADayAreRejected(int? seconds, string message)
    {
        var exception = Assert.Throws<AppProblemException>(() => ApiCaptureEndpoints.ValidateDuration(seconds));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(message, exception.Message);
    }
}
