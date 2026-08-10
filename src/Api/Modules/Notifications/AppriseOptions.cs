namespace Api.Modules.Notifications;

public sealed class AppriseOptions
{
    public string? BaseUrl { get; set; }
    public string? TelegramBotToken { get; set; }
    public bool UseMock { get; set; }
}
