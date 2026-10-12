import { Flame } from "lucide-react";
import { openGame, showToast } from "../app-state";
import { WeekBars } from "../components/shared";
import { fmtMinutes, relativeDay } from "../data/games";
import {
  daysAgo,
  libraryGame,
  playdexConnected,
  playdexSummary,
  type PlaydexSummary,
} from "../data/playdex";
import { useStore } from "../nav/store";
import "./playdex.css";

const minutes = (seconds: number) => Math.round(seconds / 60);
const clock = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" });

/**
 * The Playdex tab: the user's Playdex history from the launcher summary (GET /api/launcher/summary), so it covers
 * every platform Playdex records, not only this PC. The week, the all-time totals, every game by time played, and the
 * newest sessions. A game on this PC opens its game page; the rest are Playdex history only.
 */
export function PlaydexScreen() {
  const summary = useStore(playdexSummary);
  if (!summary) {
    return (
      <main className="placeholder">
        <div className="card">
          <span className="kicker">Playdex</span>
          <h2>{playdexConnected ? "Your Playdex stats are on their way" : "Connect Playdex to see your stats"}</h2>
          <p>
            {playdexConnected
              ? "They show up here once this PC reaches Playdex."
              : "Add your Playdex API key in Settings → Your Playdex account. The key and the server address are on the Playdex website under Settings → Integrations."}
          </p>
        </div>
      </main>
    );
  }
  return <PlaydexStats summary={summary} />;
}

function open(summary: PlaydexSummary, playdexGameId: string) {
  const local = libraryGame(summary, playdexGameId);
  if (local) openGame(local.id);
  else showToast("This game isn't on this PC.");
}

function PlaydexStats({ summary }: { summary: PlaydexSummary }) {
  const ranked = [...summary.games].sort((a, b) => b.totalSeconds - a.totalSeconds);
  const most = Math.max(ranked[0]?.totalSeconds ?? 0, 1);
  const capsule = new Map(summary.games.map((g) => [g.gameId, g.capsuleUrl]));
  const streak = summary.currentStreakDays;

  return (
    <main className="pdx">
      <div className="pdx-side">
        <section className="card pdx-card" aria-labelledby="pdx-week">
          <div className="pdx-head">
            <span id="pdx-week" className="kicker">
              This week
            </span>
            {streak > 1 && (
              <span className="rail-streak num">
                <Flame aria-hidden /> {streak}-day streak
              </span>
            )}
          </div>
          <p className="pdx-big num">{fmtMinutes(minutes(summary.week.totalSeconds))}</p>
          <p className="pdx-sub num">
            {summary.week.sessionCount} {summary.week.sessionCount === 1 ? "session" : "sessions"} ·{" "}
            {summary.today.totalSeconds > 0 ? `${fmtMinutes(minutes(summary.today.totalSeconds))} today` : "nothing yet today"}
          </p>
          <WeekBars maxHeight="7rem" minutes={summary.days.map((d) => minutes(d.totalSeconds))} />
        </section>

        <section className="card pdx-card" aria-labelledby="pdx-all">
          <span id="pdx-all" className="label">
            All time
          </span>
          <p className="pdx-mid num">{fmtMinutes(minutes(summary.allTime.totalSeconds))}</p>
          <p className="pdx-sub num">
            {summary.games.length} {summary.games.length === 1 ? "game" : "games"} · {summary.allTime.sessionCount}{" "}
            {summary.allTime.sessionCount === 1 ? "session" : "sessions"}
          </p>
        </section>
      </div>

      <section className="card pdx-list" aria-labelledby="pdx-games">
        <h2 id="pdx-games" className="pdx-title">
          Most played
        </h2>
        {ranked.length === 0 ? (
          <p className="pdx-sub">No games played yet.</p>
        ) : (
          <div className="pdx-rows" data-nav-group="pdx-games" data-scroll="y">
            {ranked.map((g, i) => {
              const local = libraryGame(summary, g.gameId);
              return (
                <button
                  key={g.gameId}
                  className="pdx-row"
                  data-nav
                  data-nav-default={i === 0 ? "" : undefined}
                  data-kind={local ? "details" : "pdx-history"}
                  data-game={local?.id}
                  onClick={() => open(summary, g.gameId)}
                >
                  <Thumb url={g.capsuleUrl} title={g.title} />
                  <span className="pdx-main">
                    <span className="pdx-name">{g.title}</span>
                    <span className="rail-meter">
                      <i style={{ width: `${(g.totalSeconds / most) * 100}%` }} />
                    </span>
                    <span className="pdx-sub num">
                      {g.sessionCount} {g.sessionCount === 1 ? "session" : "sessions"} · last played{" "}
                      {relativeDay(daysAgo(g.lastPlayedAt)).toLowerCase()}
                    </span>
                  </span>
                  <span className="pdx-val num">{fmtMinutes(minutes(g.totalSeconds))}</span>
                </button>
              );
            })}
          </div>
        )}
      </section>

      <section className="card pdx-list" aria-labelledby="pdx-sessions">
        <h2 id="pdx-sessions" className="pdx-title">
          Recent sessions
        </h2>
        {summary.recentSessions.length === 0 ? (
          <p className="pdx-sub">No sessions yet.</p>
        ) : (
          <div className="pdx-rows" data-nav-group="pdx-sessions" data-scroll="y">
            {summary.recentSessions.map((s) => {
              const local = libraryGame(summary, s.gameId);
              return (
                <button
                  key={s.sessionId}
                  className="pdx-row"
                  data-nav
                  data-kind={local ? "details" : "pdx-history"}
                  data-game={local?.id}
                  onClick={() => open(summary, s.gameId)}
                >
                  <Thumb url={capsule.get(s.gameId) ?? null} title={s.title} />
                  <span className="pdx-main">
                    <span className="pdx-name">{s.title}</span>
                    <span className="pdx-sub num">
                      {s.platformName ?? s.platformKey} · {relativeDay(daysAgo(s.startedAt))} {clock.format(new Date(s.startedAt))}
                    </span>
                  </span>
                  <span className="pdx-val num">{s.endedAt ? fmtMinutes(minutes(s.durationSeconds)) : "Playing"}</span>
                </button>
              );
            })}
          </div>
        )}
      </section>
    </main>
  );
}

/** Playdex's capsule art, or the title's initial. */
function Thumb({ url, title }: { url: string | null; title: string }) {
  return (
    <span className="rail-thumb pdx-thumb" aria-hidden>
      {url ? <img src={url} alt="" loading="lazy" /> : title.charAt(0)}
    </span>
  );
}
