import { Download, Heart, MonitorSmartphone, Play } from "lucide-react";
import { useState } from "react";
import { launch, showToast } from "../app-state";
import { Backdrop, Logo } from "../components/shared";
import {
  dailyFor,
  dayLabel,
  fmtHours,
  fmtMinutes,
  gameById,
  isDemo,
  isInstalled,
  isOnPc,
  lastPlayedDay,
  sessionEnd,
  sessionsFor,
  SOURCE_LABEL,
  statsFor,
  weekdayInitial,
} from "../data/games";
import "./game.css";

const favorites = new Set<string>(["hades2", "elden"]);

/** The game page: art and the primary action up top, the game's Playdex history below. */
export function GamePage({ gameId }: { gameId: string }) {
  const game = gameById(gameId);
  const st = statsFor(gameId);
  const lastDay = st && lastPlayedDay(st);
  const recent = sessionsFor(gameId);
  const daily = dailyFor(gameId);
  const onPc = isOnPc(game);
  const installed = isInstalled(game);
  const [fav, setFav] = useState(favorites.has(gameId));
  const maxDay = Math.max(...daily, 1);
  const playedRecently = daily.some(Boolean);

  const toggleFav = () => {
    if (fav) favorites.delete(gameId);
    else favorites.add(gameId);
    setFav(!fav);
  };

  return (
    <main className="gpage">
      <Backdrop gameId={gameId} className="gpage-art" />
      <div className="gpage-scrim" />

      <section className="gpage-hero">
        <Logo game={game} className="gpage-logo" titleClass="gpage-title" />
        <p className="gpage-meta">
          {onPc ? (
            <>
              {SOURCE_LABEL[game.source]}
              <span aria-hidden>·</span>
              {installed ? "Installed" : "Not installed"}
            </>
          ) : (
            <>
              <MonitorSmartphone aria-hidden />
              Played on {SOURCE_LABEL[game.source]} · not available on this PC
            </>
          )}
        </p>
        <div className="gpage-actions" data-nav-group="gpage-actions">
          {onPc &&
            (installed ? (
              <button
                className="btn btn-primary btn-lg"
                data-nav
                data-nav-default
                data-kind="continue"
                data-game={gameId}
                onClick={() => launch(gameId)}
              >
                <Play fill="currentColor" /> Play
              </button>
            ) : (
              <button
                className="btn btn-primary btn-lg"
                data-nav
                data-nav-default
                data-kind="install"
                data-game={gameId}
                onClick={() => showToast(`${SOURCE_LABEL[game.source]} would open to install ${game.title}`)}
              >
                <Download /> Install
              </button>
            ))}
          <button
            className="btn btn-outline btn-lg"
            data-nav
            data-nav-default={onPc ? undefined : ""}
            data-kind="favorite"
            data-game={gameId}
            aria-pressed={fav}
            onClick={toggleFav}
          >
            <Heart fill={fav ? "currentColor" : "none"} /> {fav ? "Favorite" : "Add to favorites"}
          </button>
        </div>
      </section>

      <section className="gpage-body">
        <div className="card gcard gplay">
          <h2 className="section-h">Your playtime</h2>
          <div className="gplay-top">
            <div className="gbig">
              <b className="num">{fmtHours(game.totalMinutes)}</b>
              <span>played in total</span>
            </div>
            <dl className="gfacts">
              <div>
                <dt className="label">This week</dt>
                <dd className="num">{st?.weekMinutes ? fmtMinutes(st.weekMinutes) : "—"}</dd>
              </div>
              <div>
                <dt className="label">Last played</dt>
                <dd>{lastDay !== undefined ? dayLabel(lastDay) : isDemo ? "Over two weeks ago" : "—"}</dd>
              </div>
              <div>
                <dt className="label">Sessions, 14 days</dt>
                <dd className="num">{recent.length}</dd>
              </div>
            </dl>
          </div>
          {playedRecently && (
          <div className="gdays" aria-label="Minutes played per day, last 14 days">
            {daily.map((m, i) => (
              <div key={i} className="gday" data-today={i === 13 ? "" : undefined}>
                <div className="gday-bar">
                  <i style={{ transform: `scaleY(${m / maxDay})` }} />
                </div>
                <span>{weekdayInitial(13 - i)}</span>
              </div>
            ))}
          </div>
          )}
        </div>

        <div className="card gcard gsessions">
          <h2 className="section-h">Recent sessions</h2>
          {recent.length ? (
            <ol>
              {recent.slice(0, 5).map((s) => (
                <li key={`${s.dayOffset}-${s.start}`}>
                  <span className="gs-day">{dayLabel(s.dayOffset)}</span>
                  <span className="gs-time num">
                    {s.start}–{sessionEnd(s)}
                  </span>
                  <span className="gs-dur num">{fmtMinutes(s.minutes)}</span>
                </li>
              ))}
            </ol>
          ) : (
            <p className="gs-empty">
              {isDemo
                ? `No sessions in the last two weeks. Playdex holds ${fmtHours(game.totalMinutes)} of history for this game.`
                : "No sessions recorded from this PC yet."}
            </p>
          )}
          <p className="gs-note">
            {onPc
              ? "Playing from here records the session to Playdex automatically."
              : `Synced from your ${SOURCE_LABEL[game.source]} account.`}
          </p>
        </div>
      </section>
    </main>
  );
}
