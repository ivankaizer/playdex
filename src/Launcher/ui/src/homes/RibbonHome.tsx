import { Flame, Play } from "lucide-react";
import { launch } from "../app-state";
import { Backdrop, Cover, Logo, WeekBars } from "../components/shared";
import {
  fmtMinutes,
  isDemo,
  lastPlayedDay,
  lastPlayedText,
  recentGames,
  relativeDay,
  sessionEnd,
  SOURCE_LABEL,
} from "../data/games";
import { daysAgo, playdexConnected, playdexGame, playdexSummary, type PlaydexSummary } from "../data/playdex";
import { useStore } from "../nav/store";
import "./ribbon.css";

const minutes = (seconds: number) => Math.round(seconds / 60);

/**
 * Home A: one grid of recent games, led by the last played as a large tile with its art, session and Continue, so
 * it reads as the first game of the list rather than a banner above it. The rail shows the user's Playdex stats.
 */
export function RibbonHome() {
  const summary = useStore(playdexSummary);
  const [last, ...rest] = recentGames;
  const others = rest.slice(0, 4);
  const s = last.lastSession;
  const lastDay = lastPlayedDay(last);
  const played = playdexGame(summary, last.game);

  return (
    <main className="ribbon-home">
      <Backdrop className="ribbon-backdrop" />

      <section className="jump" aria-labelledby="jump-h">
        <h2 id="jump-h" className="section-h">
          Jump back in
        </h2>
        <div className="jump-grid" data-nav-group="jump">
          <button
            className="ribbon"
            data-nav
            data-nav-default
            data-game={last.game.id}
            data-kind="continue"
            onClick={() => launch(last.game.id)}
          >
            <Backdrop gameId={last.game.id} className="ribbon-art" />
            <span className="ribbon-scrim" aria-hidden />
            <span className="ribbon-body">
              <span className="kicker">
                {s
                  ? `Last played · ${relativeDay(s.dayOffset)} ${s.start}–${sessionEnd(s)}`
                  : lastDay !== undefined
                    ? `Last played · ${relativeDay(lastDay)}`
                    : "Ready to play"}
              </span>
              <Logo game={last.game} className="ribbon-logo" titleClass="ribbon-title" />
              {played ? (
                <span className="ribbon-meta num">
                  <b>{fmtMinutes(minutes(played.totalSeconds))}</b> played
                  <span aria-hidden>·</span>
                  {played.sessionCount} {played.sessionCount === 1 ? "session" : "sessions"}
                  <span aria-hidden>·</span>
                  {SOURCE_LABEL[last.game.source]}
                </span>
              ) : (
                <span className="ribbon-meta">{SOURCE_LABEL[last.game.source]} · Installed</span>
              )}
              {/* The whole tile is the button; this is its label. */}
              <span className="btn btn-primary btn-lg ribbon-cta" aria-hidden>
                <Play fill="currentColor" /> Continue
              </span>
            </span>
          </button>
          {others.map((r) => (
            <button
              key={r.game.id}
              className="jtile"
              data-nav
              data-game={r.game.id}
              data-kind="game"
              onClick={() => launch(r.game.id)}
            >
              <Cover game={r.game} kind="capsule" className="jtile-art" />
              <span className="jtile-foot">
                <span className="jtile-title">{r.game.title}</span>
                <span className="jtile-sub">
                  {lastPlayedText(r)} · {SOURCE_LABEL[r.game.source]}
                </span>
              </span>
            </button>
          ))}
        </div>
      </section>

      {summary ? <PlaydexRail summary={summary} /> : <EmptyRail />}
    </main>
  );
}

/** Only Playdex data: what the user's sessions in Playdex add up to, in this PC's time zone. */
function PlaydexRail({ summary }: { summary: PlaydexSummary }) {
  const top = summary.topGames.slice(0, 3);
  const topMax = Math.max(...top.map((t) => t.totalSeconds), 1);
  const capsule = new Map(summary.games.map((g) => [g.gameId, g.capsuleUrl]));
  const last = summary.recentSessions[0];
  const streak = summary.currentStreakDays;

  return (
    <aside className="card rail" aria-label="Playdex">
      <div className="rail-block">
        <div className="rail-row">
          <span className="kicker">Today</span>
          {streak > 1 && (
            <span className="rail-streak num">
              <Flame aria-hidden /> {streak}-day streak
            </span>
          )}
        </div>
        <p className="rail-big num">{fmtMinutes(minutes(summary.today.totalSeconds))}</p>
        <p className="rail-sub">
          {summary.today.sessionCount === 0
            ? "Nothing played yet today"
            : `across ${summary.today.sessionCount} ${summary.today.sessionCount === 1 ? "session" : "sessions"}`}
        </p>
      </div>

      <div className="rail-block">
        <div className="rail-row">
          <span className="label">This week</span>
          <span className="rail-val num">{fmtMinutes(minutes(summary.week.totalSeconds))}</span>
        </div>
        <WeekBars maxHeight="4.4rem" minutes={summary.days.map((d) => minutes(d.totalSeconds))} />
      </div>

      {top.length > 0 && (
        <div className="rail-block rail-top">
          <span className="label">Most played this week</span>
          <ol>
            {top.map((t) => (
              <li key={t.gameId}>
                <span className="rail-thumb" aria-hidden>
                  {capsule.get(t.gameId) ? <img src={capsule.get(t.gameId)!} alt="" loading="lazy" /> : t.title.charAt(0)}
                </span>
                <span className="rail-game">
                  <span className="rail-name">{t.title}</span>
                  <span className="rail-meter">
                    <i style={{ width: `${(t.totalSeconds / topMax) * 100}%` }} />
                  </span>
                </span>
                <span className="rail-val num">{fmtMinutes(minutes(t.totalSeconds))}</span>
              </li>
            ))}
          </ol>
        </div>
      )}

      {last && (
        <div className="rail-block">
          <span className="label">Last session</span>
          <p className="rail-last">
            <span className="rail-name">{last.title}</span>
            <span className="rail-sub num">
              {last.endedAt ? fmtMinutes(minutes(last.durationSeconds)) : "Playing now"} · {relativeDay(daysAgo(last.startedAt))}
            </span>
          </p>
        </div>
      )}

      <div className="rail-block">
        <span className="label">All time</span>
        <div className="rail-row">
          <span className="rail-mid num">{fmtMinutes(minutes(summary.allTime.totalSeconds))}</span>
          <span className="rail-sub num">
            {summary.games.length} {summary.games.length === 1 ? "game" : "games"} · {summary.allTime.sessionCount} sessions
          </span>
        </div>
      </div>

      {isDemo && <p className="rail-note">Sample data</p>}
    </aside>
  );
}

function EmptyRail() {
  return (
    <aside className="card rail rail-empty" aria-label="Playdex">
      <span className="kicker">Playdex</span>
      <p className="rail-sub">
        {playdexConnected
          ? "Your Playdex stats show up here once this PC reaches Playdex."
          : "Add your Playdex API key in the Playdex desktop app (Settings) to see your stats here."}
      </p>
    </aside>
  );
}
