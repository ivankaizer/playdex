using Playdex.Persistence;

namespace Api.Infrastructure.Common;

public sealed class ClassifiedPlaySession
{
    public required PlaySession Session { get; init; }
    public bool Ignored { get; init; }
}

public static class PlaySessionQueries
{
    /// <summary>
    /// Play sessions flagged as ignored when they finished in less time than the owner's
    /// ignore-short-sessions setting (capped at a day, like the setting itself, for values stored before the cap).
    /// </summary>
    /// <param name="runningUntil">
    /// When set, a session that is still running counts as ending then, so one that has not yet run the setting's
    /// minutes is ignored too. Without it a running session is never ignored.
    /// </param>
    public static IQueryable<ClassifiedPlaySession> ClassifiedPlaySessions(
        this PlaydexDbContext db, DateTimeOffset? runningUntil = null) =>
        db.PlaySessions.LeftJoin(
            db.UserSettings,
            ps => ps.UserId,
            us => us.UserId,
            (ps, us) => new ClassifiedPlaySession
            {
                Session = ps,
                Ignored = us != null
                    && us.IgnoreShortSessionsMinutes > 0
                    && (ps.EndedAt ?? runningUntil) != null
                    && (ps.EndedAt ?? runningUntil) < ps.StartedAt.AddMinutes(
                        Math.Min(us.IgnoreShortSessionsMinutes, SessionLimits.MaxDurationMinutes)),
            });
}
