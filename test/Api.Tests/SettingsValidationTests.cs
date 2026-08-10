using Api.Infrastructure.Http;
using Api.Modules.Settings;
using Xunit;

namespace Api.Tests;

public sealed class SettingsValidationTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" 123456789 ", "123456789")]
    [InlineData("-100123456789", "-100123456789")]
    public void TelegramChatIdIsNormalized(string? value, string? expected) =>
        Assert.Equal(expected, SettingsService.ValidateTelegramChatId(value));

    [Theory]
    [InlineData("0")]
    [InlineData("@username")]
    [InlineData("12.5")]
    public void InvalidTelegramChatIdIsRejected(string value) =>
        Assert.Throws<AppProblemException>(() =>
            SettingsService.ValidateTelegramChatId(value));
}
