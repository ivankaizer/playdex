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
    public static IQueryable<ClassifiedPlaySession> ClassifiedPlaySessions(this PlaydexDbContext db) =>
        db.PlaySessions.LeftJoin(
            db.UserSettings,
            ps => ps.UserId,
            us => us.UserId,
            (ps, us) => new ClassifiedPlaySession
            {
                Session = ps,
                Ignored = us != null
                    && us.IgnoreShortSessionsMinutes > 0
                    && ps.EndedAt != null
                    && ps.EndedAt < ps.StartedAt.AddMinutes(
                        Math.Min(us.IgnoreShortSessionsMinutes, SessionLimits.MaxDurationMinutes)),
            });
}
