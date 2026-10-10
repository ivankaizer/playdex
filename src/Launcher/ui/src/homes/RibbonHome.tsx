import { Play } from "lucide-react";
import { launch } from "../app-state";
import { Backdrop, Cover, Logo, WeekBars } from "../components/shared";
import {
  fmtHours,
  fmtMinutes,
  isDemo,
  lastPlayedDay,
  lastPlayedText,
  recentGames,
  relativeDay,
  sessionEnd,
  SOURCE_LABEL,
  todayMinutes,
  todaySessions,
  weekMinutes,
} from "../data/games";
import "./ribbon.css";

/**
 * Home A: one grid of recent games, led by the last played as a large tile with its art, session and Continue, so
 * it reads as the first game of the list rather than a banner above it.
 */
export function RibbonHome() {
  const [last, ...rest] = recentGames;
  const others = rest.slice(0, 4);
  const top = recentGames.filter((r) => r.weekMinutes > 0).sort((a, b) => b.weekMinutes - a.weekMinutes).slice(0, 3);
  const s = last.lastSession;
  const lastDay = lastPlayedDay(last);

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
              {s ? (
                <span className="ribbon-meta num">
                  <b>{fmtMinutes(s.minutes)}</b> last session
                  <span aria-hidden>·</span>
                  {fmtMinutes(last.weekMinutes)} this week
                  <span aria-hidden>·</span>
                  {fmtHours(last.game.totalMinutes)} total
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

      <aside className="card rail" aria-label="Playdex">
        <div className="rail-block">
          <span className="kicker">Today</span>
          <p className="rail-big num">{fmtMinutes(todayMinutes)}</p>
          <p className="rail-sub">across {todaySessions} sessions</p>
        </div>
        <div className="rail-block">
          <div className="rail-row">
            <span className="label">This week</span>
            <span className="rail-val num">{fmtMinutes(weekMinutes)}</span>
          </div>
          <WeekBars maxHeight="4.4rem" />
        </div>
        {top.length > 0 && (
          <div className="rail-block rail-top">
            <span className="label">Most played this week</span>
            <ol>
              {top.map((r) => (
                <li key={r.game.id}>
                  <Cover game={r.game} kind="portrait" className="rail-thumb" compact />
                  <span className="rail-name">{r.game.title}</span>
                  <span className="rail-val num">{fmtMinutes(r.weekMinutes)}</span>
                </li>
              ))}
            </ol>
          </div>
        )}
        <p className="rail-note">{isDemo ? "Sample data" : "No Playdex history here yet"}</p>
      </aside>
    </main>
  );
}
