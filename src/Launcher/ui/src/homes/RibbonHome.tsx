import { Flame, Play } from "lucide-react";
import { launch, nowPlaying, resumeGame } from "../app-state";
import { SaveGlyph, useSaves } from "../components/saves";
import { Backdrop, Cover, formatClock, Logo, useElapsed, WeekBars } from "../components/shared";
import {
  fmtMinutes,
  gameById,
  isDemo,
  lastPlayedDay,
  lastPlayedText,
  relativeDay,
  sessionEnd,
  SOURCE_LABEL,
  useRecentGames,
  type GameStats,
} from "../data/games";
import { daysAgo, playdexConnected, playdexGame, playdexSummary, type PlaydexSummary } from "../data/playdex";
import { useStore } from "../nav/store";
import "./ribbon.css";

const minutes = (seconds: number) => Math.round(seconds / 60);

/** The running game first, then the rest in their order: a game started elsewhere may not be the last played yet. */
function runningFirst(recent: GameStats[], gameId: string | undefined): GameStats[] {
  if (!gameId || recent[0]?.game.id === gameId) return recent;
  const lead = recent.find((r) => r.game.id === gameId) ?? { game: gameById(gameId), weekMinutes: 0, weekSessions: 0 };
  return [lead, ...recent.filter((r) => r !== lead)];
}

/**
 * Home A: one grid of recent games, led by the last played as a large tile with its art, session and Continue, so
 * it reads as the first game of the list rather than a banner above it. While a game runs it leads instead, and the
 * tile says so and resumes it, as the top bar does. A game added to Playdex says where its save backups stand. The
 * rail shows the user's Playdex stats.
 */
export function RibbonHome() {
  const summary = useStore(playdexSummary);
  const running = useStore(nowPlaying);
  const [last, ...rest] = runningFirst(useRecentGames(), running?.gameId);
  const others = rest.slice(0, 4);
  const s = last.lastSession;
  const lastDay = lastPlayedDay(last);
  const played = playdexGame(summary, last.game);
  const playing = running?.gameId === last.game.id;
  const elapsed = useElapsed(playing ? running.since : null);
  const saves = useSaves(last.game.id)?.status;
  const savesNote = saves && (
    <>
      <span aria-hidden>·</span>
      <span className="save-note">
        <SaveGlyph kind={saves.kind} />
        {saves.label}
      </span>
    </>
  );

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
            data-kind={playing ? "playing" : "continue"}
            onClick={() => (playing ? resumeGame() : launch(last.game.id))}
          >
            <Backdrop gameId={last.game.id} className="ribbon-art" />
            <span className="ribbon-scrim" aria-hidden />
            <span className="ribbon-body">
              {playing ? (
                <span className="kicker ribbon-live">
                  <span className="live-dot" aria-hidden />
                  Playing now
                  {elapsed !== null && <span className="num">· {formatClock(elapsed)}</span>}
                </span>
              ) : (
                <span className="kicker">
                  {s
                    ? `Last played · ${relativeDay(s.dayOffset)} ${s.start}–${sessionEnd(s)}`
                    : lastDay !== undefined
                      ? `Last played · ${relativeDay(lastDay)}`
                      : "Ready to play"}
                </span>
              )}
              <Logo game={last.game} className="ribbon-logo" titleClass="ribbon-title" />
              {played ? (
                <span className="ribbon-meta num">
                  <b>{fmtMinutes(minutes(played.totalSeconds))}</b> played
                  <span aria-hidden>·</span>
                  {played.sessionCount} {played.sessionCount === 1 ? "session" : "sessions"}
                  <span aria-hidden>·</span>
                  {SOURCE_LABEL[last.game.source]}
                  {savesNote}
                </span>
              ) : (
                <span className="ribbon-meta">
                  {SOURCE_LABEL[last.game.source]}
                  <span aria-hidden>·</span>
                  Installed
                  {savesNote}
                </span>
              )}
              {/* The whole tile is the button; this is its label. */}
              <span className="btn btn-primary btn-lg ribbon-cta" aria-hidden>
                <Play fill="currentColor" /> {playing ? "Resume" : "Continue"}
              </span>
            </span>
          </button>
          {others.map((r) => {
            // Total time from Playdex, as on the large tile; the source when Playdex has none for the game.
            const total = playdexGame(summary, r.game)?.totalSeconds ?? 0;
            return (
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
                  <span className="jtile-sub num">
                    {lastPlayedText(r)} · {total > 0 ? <><b>{fmtMinutes(minutes(total))}</b> played</> : SOURCE_LABEL[r.game.source]}
                  </span>
                </span>
              </button>
            );
          })}
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
          : "Add your Playdex API key in Settings → Your Playdex account to see your stats here."}
      </p>
    </aside>
  );
}
