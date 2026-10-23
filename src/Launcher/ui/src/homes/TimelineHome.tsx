import { Info, Play } from "lucide-react";
import { launch, openGame } from "../app-state";
import { Backdrop, Cover, Logo } from "../components/shared";
import {
  dayLabel,
  fmtHours,
  fmtMinutes,
  gameById,
  lastPlayedDay,
  recentGames,
  relativeDay,
  sessionEnd,
  sessions,
  sessionsNewestFirst,
  SOURCE_LABEL,
  statsFor,
  type Session,
} from "../data/games";
import { createStore, useStore } from "../nav/store";
import "./timeline.css";

const DAYS = 14;

/** Which session chip is focused (index into sessionsNewestFirst). */
const focusedSession = createStore(0);

/** Home C: home is your play history. Days run left to right from today; each holds that day's sessions. */
export function TimelineHome() {
  const days = Array.from({ length: DAYS }, (_, d) => ({
    offset: d,
    sessions: sessionsNewestFirst.filter((s) => s.dayOffset === d),
  }));

  return (
    <main className="timeline-home">
      <Backdrop className="stage-art" />
      <div className="stage-scrim" />
      <Stage />

      <section className="lanes-wrap" aria-label="Play history">
        <div className="lanes" data-scroll="x" data-nav-group="lanes">
          {days.map((day) => {
            const total = day.sessions.reduce((t, s) => t + s.minutes, 0);
            return (
              <div key={day.offset} className="lane" data-empty={total ? undefined : ""}>
                <header className="lane-head">
                  <span className={day.offset === 0 ? "kicker" : "label"}>{dayLabel(day.offset)}</span>
                  <span className="lane-total num">{total ? fmtMinutes(total) : "No play"}</span>
                </header>
                <div className="lane-chips">
                  {day.sessions.map((s) => (
                    <Chip key={`${s.gameId}-${s.start}`} s={s} />
                  ))}
                </div>
              </div>
            );
          })}
          <div className="lane lane-end">
            <span className="label">Older sessions are in Playdex</span>
          </div>
        </div>
      </section>
    </main>
  );
}

function Chip({ s }: { s: Session }) {
  const game = gameById(s.gameId);
  const idx = sessionsNewestFirst.indexOf(s);
  // Chip height scales with time played: 30 min is a compact chip, 3 h fills most of a lane.
  const h = Math.min(7.5, 2.9 + (s.minutes / 60) * 1.4);
  return (
    <button
      className="chip"
      style={{ height: `${h}rem` }}
      data-nav
      data-nav-default={idx === 0 ? "" : undefined}
      data-game={s.gameId}
      data-kind="session"
      onFocus={() => focusedSession.set(idx)}
      onClick={() => launch(s.gameId)}
    >
      <Cover game={game} kind="portrait" className="chip-thumb" compact />
      <span className="chip-text">
        <span className="chip-title">{game.title}</span>
        <span className="chip-time num">
          {s.start}–{sessionEnd(s)}
        </span>
      </span>
      <span className="chip-dur num">{fmtMinutes(s.minutes)}</span>
    </button>
  );
}

function Stage() {
  return sessionsNewestFirst.length ? <SessionStage /> : <GameStage />;
}

/** No sessions to show (the host library has no Playdex history yet): feature the most recently played game. */
function GameStage() {
  const st = recentGames[0];
  const game = st.game;
  const lastDay = lastPlayedDay(st);
  return (
    <section className="stage" aria-live="polite" aria-label="Selected game">
      <div className="stage-body">
        <span className="kicker">{lastDay === undefined ? "Ready to play" : `Last played ${relativeDay(lastDay).toLowerCase()}`}</span>
        <Logo game={game} className="stage-logo" titleClass="stage-title" />
        <p className="stage-meta">
          {SOURCE_LABEL[game.source]}
          <span aria-hidden>·</span>
          Installed
        </p>
        <div className="stage-actions" data-nav-group="stage-actions">
          <button
            className="btn btn-primary btn-lg"
            data-nav
            data-nav-default
            data-kind="continue"
            data-game={game.id}
            onClick={() => launch(game.id)}
          >
            <Play fill="currentColor" /> Play
          </button>
          <button className="btn btn-outline btn-lg" data-nav data-kind="details" data-game={game.id} onClick={() => openGame(game.id)}>
            <Info /> Details
          </button>
        </div>
      </div>
    </section>
  );
}

function SessionStage() {
  const idx = useStore(focusedSession);
  const s = sessionsNewestFirst[idx] ?? sessionsNewestFirst[0];
  const game = gameById(s.gameId);
  const st = statsFor(s.gameId)!;
  const played = sessions.filter((x) => x.gameId === s.gameId && x.dayOffset < DAYS).length;
  const isLatest = idx === 0;

  return (
    <section className="stage" aria-live="polite" aria-label="Selected session">
      <div className="stage-body" key={idx}>
        <span className="kicker">
          {dayLabel(s.dayOffset)} · {s.start}–{sessionEnd(s)} · {fmtMinutes(s.minutes)}
        </span>
        <Logo game={game} className="stage-logo" titleClass="stage-title" />
        <p className="stage-meta num">
          {fmtMinutes(st.weekMinutes)} in the last 7 days
          <span aria-hidden>·</span>
          {played} {played === 1 ? "session" : "sessions"} in two weeks
          <span aria-hidden>·</span>
          {fmtHours(game.totalMinutes)} total
          <span aria-hidden>·</span>
          {SOURCE_LABEL[game.source]}
        </p>
        <div className="stage-actions" data-nav-group="stage-actions">
          <button className="btn btn-primary btn-lg" data-nav data-kind="continue" data-game={s.gameId} onClick={() => launch(s.gameId)}>
            <Play fill="currentColor" /> {isLatest ? "Continue" : "Play"}
          </button>
          <button className="btn btn-outline btn-lg" data-nav data-kind="details" data-game={s.gameId} onClick={() => openGame(s.gameId)}>
            <Info /> Details
          </button>
        </div>
      </div>
    </section>
  );
}
