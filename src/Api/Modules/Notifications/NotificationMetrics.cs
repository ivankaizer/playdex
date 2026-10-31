using System.Diagnostics.Metrics;

namespace Api.Modules.Notifications;

/// <summary>What became of a notification: sent, or skipped and why.</summary>
/// <param name="UserId">Whom it was for; null when that could not be resolved.</param>
public sealed record NotificationOutcome(long? UserId, string? SkipReason)
{
    public const string Disabled = "notifications_disabled";
    public const string NoTarget = "no_target";
    public const string NoChannel = "no_channel";
    public const string IgnoredGame = "ignored_game";
    public const string OverlappingSession = "overlapping_session";
    public const string RetiredKind = "retired_kind";

    public bool Sent => SkipReason is null;

    public static NotificationOutcome Delivered(long userId) => new(userId, null);

    public static NotificationOutcome Skipped(long? userId, string reason) => new(userId, reason);
}

/// <summary>
/// <c>playdex.notifications</c>: every notification once it is sent, skipped, retried or given up. A user whose
/// sessions are counted but whose notifications are not is a user who is not being told.
/// </summary>
public static class NotificationMetrics
{
    public const string MeterName = "Playdex.Api.Notifications";

    private static readonly Meter _meter = new(MeterName);
    private static readonly Counter<long> _notifications = _meter.CreateCounter<long>(
        "playdex.notifications",
        description: "Notifications by kind and outcome: sent, skipped (with a reason), retrying or failed.");

    public static void Record(string kind, NotificationOutcome outcome) =>
        _notifications.Add(
            1,
            new("kind", kind),
            new("outcome", outcome.Sent ? "sent" : "skipped"),
            new("reason", outcome.SkipReason ?? ""),
            new("user_id", outcome.UserId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));

    /// <param name="gaveUp">No more attempts: the user will not get it.</param>
    public static void RecordFailure(string kind, bool gaveUp) =>
        _notifications.Add(
            1,
            new("kind", kind),
            new("outcome", gaveUp ? "failed" : "retrying"),
            new("reason", ""),
            new("user_id", ""));
}
