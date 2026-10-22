import { Archive, CalendarCheck, CalendarX, Download, Heart, ImageIcon, MonitorSmartphone, Play, X } from "lucide-react";
import { useState } from "react";
import { closeArmed, forceCloseGame, launch, openArtPicker, resumeGame, showToast, useIsRunning } from "../app-state";
import { SaveGlyph, useSaves } from "../components/saves";
import { Backdrop, Logo } from "../components/shared";
import {
  dayLabel,
  fmtHours,
  fmtMinutes,
  gameById,
  isDemo,
  isInstalled,
  isOnPc,
  sessionEnd,
  SOURCE_LABEL,
  weekdayInitial,
} from "../data/games";
import { toggleFavorite, useFavorites } from "../data/favorites";
import { gameHistory, playdexConnected, playdexSummary } from "../data/playdex";
import { rpc, type MessageView } from "../host/desktop";
import { useStore } from "../nav/store";
import "./game.css";

/**
 * The game page: art and the primary action up top, the game's Playdex history below. While the game runs, Play
 * resumes it and Close game force-closes it. A game added to Playdex also shows its save backups and can back them up
 * now or daily. Any game on this PC can have its artwork changed to images from SteamGridDB.
 */
export function GamePage({ gameId }: { gameId: string }) {
  const game = gameById(gameId);
  const history = gameHistory(useStore(playdexSummary), game);
  const lastDay = history.lastPlayedDay;
  const recent = history.sessions;
  const daily = history.daily;
  const onPc = isOnPc(game);
  const installed = isInstalled(game);
  const fav = useFavorites().has(gameId);
  const maxDay = Math.max(...daily, 1);
  const playedRecently = daily.some(Boolean);
  const saves = useSaves(gameId);
  const playing = useIsRunning(gameId);
  const closeConfirm = useStore(closeArmed) === gameId;

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
              {saves?.status && (
                <>
                  <span aria-hidden>·</span>
                  <span className="save-note">
                    <SaveGlyph kind={saves.status.kind} />
                    {saves.status.label}
                  </span>
                </>
              )}
              {saves && !saves.ready && (
                <>
                  <span aria-hidden>·</span>
                  Set up Ludusavi in Settings to back up saves
                </>
              )}
            </>
          ) : (
            <>
              <MonitorSmartphone aria-hidden />
              Played on {SOURCE_LABEL[game.source]} · not available on this PC
            </>
          )}
        </p>
        <div className="gpage-actions" data-nav-group="gpage-actions">
          {playing ? (
            <>
              <button
                className="btn btn-primary btn-lg"
                data-nav
                data-nav-default
                data-kind="playing"
                data-game={gameId}
                onClick={() => resumeGame(gameId)}
              >
                <Play fill="currentColor" /> Resume
              </button>
              <button
                className="btn btn-outline btn-lg gpage-close"
                data-nav
                data-kind="close-game"
                data-game={gameId}
                data-armed={closeConfirm ? "" : undefined}
                aria-label={closeConfirm ? `Close ${game.title}? Press again to confirm` : `Close ${game.title}`}
                onClick={() => forceCloseGame(gameId)}
              >
                <X /> {closeConfirm ? "Close game?" : "Close game"}
              </button>
            </>
          ) : onPc &&
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
            onClick={() => toggleFavorite(gameId)}
          >
            <Heart fill={fav ? "currentColor" : "none"} /> {fav ? "Favorite" : "Add to favorites"}
          </button>
          {onPc && (
            <button className="btn btn-outline btn-lg" data-nav data-kind="artwork" data-game={gameId} onClick={() => openArtPicker(gameId)}>
              <ImageIcon /> Artwork
            </button>
          )}
          {saves?.ready && <SaveActions gameId={gameId} configId={saves.game.gameId} daily={saves.game.dailyBackup} running={saves.game.backup.kind === "running"} />}
        </div>
      </section>

      <section className="gpage-body">
        <div className="card gcard gplay">
          <h2 className="section-h">Your playtime</h2>
          <div className="gplay-top">
            <div className="gbig">
              <b className="num">{fmtHours(history.totalMinutes)}</b>
              <span>played in total</span>
            </div>
            <dl className="gfacts">
              <div>
                <dt className="label">Last 7 days</dt>
                <dd className="num">{history.last7Minutes ? fmtMinutes(history.last7Minutes) : "—"}</dd>
              </div>
              <div>
                <dt className="label">Last played</dt>
                <dd>{lastDay !== undefined ? dayLabel(lastDay) : isDemo ? "Over two weeks ago" : "—"}</dd>
              </div>
              <div>
                <dt className="label">Sessions, 14 days</dt>
                <dd className="num">{history.sessionCount}</dd>
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
              {recent.map((s) => (
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
              {history.inPlaydex
                ? `No sessions in the last two weeks. Playdex holds ${fmtHours(history.totalMinutes)} of history for this game.`
                : isDemo || playdexConnected
                  ? "No sessions in Playdex yet."
                  : "Add your Playdex API key in Settings to see this game's history."}
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

/** Back up now, and daily backup on or off. A backup runs in the desktop app; its status shows above as it goes. */
function SaveActions({ gameId, configId, daily, running }: { gameId: string; configId: string; daily: boolean; running: boolean }) {
  const [busy, setBusy] = useState(false);
  const request = async (work: () => Promise<void>) => {
    if (busy) return;
    setBusy(true);
    try {
      await work();
    } catch (e) {
      showToast(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };
  const backUp = () =>
    request(async () => {
      if (running) return;
      const r = await rpc<MessageView>("game.backup", { gameId: configId });
      showToast(r.message);
    });
  const toggleDaily = () =>
    request(async () => {
      await rpc("game.dailyBackup", { gameId: configId, enabled: !daily });
      showToast(daily ? "Daily backup is off" : "Saves will be backed up daily");
    });

  return (
    <>
      <button className="btn btn-outline btn-lg" data-nav data-kind="backup" data-game={gameId} aria-busy={running} onClick={backUp}>
        <Archive /> {running ? "Backing up…" : "Back up saves"}
      </button>
      <button className="btn btn-outline btn-lg" data-nav data-kind="daily-backup" data-game={gameId} aria-pressed={daily} onClick={toggleDaily}>
        {daily ? <CalendarCheck /> : <CalendarX />} Daily backup {daily ? "on" : "off"}
      </button>
    </>
  );
}
