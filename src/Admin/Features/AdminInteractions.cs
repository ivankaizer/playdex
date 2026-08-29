using Radzen;

namespace Admin.Features;

public interface IAdminInteractions
{
    Task<bool> ConfirmAsync(string message, string title);
    void Success(string summary, string detail);
    void Error(string summary, string detail);
}

public sealed class RadzenAdminInteractions(DialogService dialogs, NotificationService notifications) : IAdminInteractions
{
    public async Task<bool> ConfirmAsync(string message, string title) =>
        await dialogs.Confirm(message, title) == true;

    public void Success(string summary, string detail) =>
        notifications.Notify(NotificationSeverity.Success, summary, detail);

    public void Error(string summary, string detail) =>
        notifications.Notify(NotificationSeverity.Error, summary, detail);
}
