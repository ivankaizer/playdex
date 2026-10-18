import { Archive, Heart, MonitorSmartphone, Settings2 } from "lucide-react";
import { useState } from "react";
import { showToast } from "../app-state";
import { SaveGlyph, useSaves, type Saves } from "../components/saves";
import { Cover, Logo } from "../components/shared";
import { toggleFavorite, useFavorites } from "../data/favorites";
import { dayLabel, fmtHours, fmtMinutes, gameById, isDemo, isInstalled, isOnPc, sessionEnd, SOURCE_LABEL, type Game } from "../data/games";
import { gameHistory, playdexConnected, playdexSummary, type GameHistory } from "../data/playdex";
import { rpc, type MessageView } from "../host/desktop";
import { useStore } from "../nav/store";
import { DayBars, PlayButton, Stat } from "./parts";
import { goTo } from "./state";

/**
 * A game's page in desktop mode, as in Steam's library: its hero art with the logo, a bar with Play and the headline
 * figures, then its Playdex history beside its save backups and where it comes from.
 */
export function GameView({ gameId }: { gameId: string }) {
  const game = gameById(gameId);
  const history = gameHistory(useStore(playdexSummary), game);
  const saves = useSaves(gameId);
  const favorite = useFavorites().has(gameId);
  const lastDay = history.lastPlayedDay;

  return (
    <article className="dk-game" aria-labelledby="dk-game-title">
      <header className="dk-hero">
        <Cover game={game} kind="hero" className="dk-hero-art" eager />
        <div className="dk-hero-scrim" />
        <div className="dk-hero-logo" id="dk-game-title">
          <Logo game={game} className="dk-logo" titleClass="dk-hero-title" />
        </div>
      </header>

      <div className="dk-playbar" data-nav-group="game-actions">
        <PlayButton game={game} />
        <Stat label="Last played">{lastDay !== undefined ? dayLabel(lastDay) : isDemo ? "Over two weeks ago" : "—"}</Stat>
        <Stat label="Play time">{history.totalMinutes ? fmtMinutes(history.totalMinutes) : "—"}</Stat>
        <Stat label="Last 7 days">{history.last7Minutes ? fmtMinutes(history.last7Minutes) : "—"}</Stat>
        <Stat label="Source">{SOURCE_LABEL[game.source]}</Stat>
        <span className="dk-spacer" />
        <button
          className="dk-icon-btn"
          data-nav
          data-kind="favorite"
          aria-pressed={favorite}
          aria-label={favorite ? "Remove from favorites" : "Add to favorites"}
          title={favorite ? "Remove from favorites" : "Add to favorites"}
          onClick={() => toggleFavorite(gameId)}
        >
          <Heart fill={favorite ? "currentColor" : "none"} aria-hidden />
        </button>
        {saves && (
          <button className="dk-icon-btn" data-nav data-kind="desk" aria-label="Game settings in Manage" title="Game settings in Manage" onClick={() => goTo("Manage")}>
            <Settings2 aria-hidden />
          </button>
        )}
      </div>

      <div className="dk-game-body">
        <div className="dk-game-main">
          <Playtime history={history} />
          <Sessions game={game} history={history} />
        </div>
        <div className="dk-game-side">
          {saves && <SavesCard saves={saves} />}
          <Details game={game} />
        </div>
      </div>
    </article>
  );
}

function Playtime({ history }: { history: GameHistory }) {
  const fortnight = history.daily.reduce((t, m) => t + m, 0);
  return (
    <section className="dk-card" aria-labelledby="dk-playtime">
      <div className="dk-card-head">
        <h2 id="dk-playtime">Your playtime</h2>
        <span className="dk-label">Last 14 days</span>
        <span className="dk-card-figure num">{fortnight ? fmtMinutes(fortnight) : "—"}</span>
      </div>
      {fortnight ? (
        <DayBars minutes={history.daily} label="Minutes played per day, last 14 days" />
      ) : (
        <p className="dk-muted">
          {history.inPlaydex ? `Not played in the last two weeks. ${fmtHours(history.totalMinutes)} in total.` : "No play in Playdex yet."}
        </p>
      )}
    </section>
  );
}

function Sessions({ game, history }: { game: Game; history: GameHistory }) {
  return (
    <section className="dk-card" aria-labelledby="dk-sessions">
      <div className="dk-card-head">
        <h2 id="dk-sessions">Recent sessions</h2>
        {history.sessionCount > 0 && <span className="dk-muted num">{history.sessionCount} in 14 days</span>}
      </div>
      {history.sessions.length ? (
        <ol className="dk-sessions">
          {history.sessions.map((s) => (
            <li key={`${s.dayOffset}-${s.start}`}>
              <span className="dk-sessions-day">{dayLabel(s.dayOffset)}</span>
              <span className="dk-muted num">
                {s.start}–{sessionEnd(s)}
              </span>
              <span className="dk-sessions-dur num">{fmtMinutes(s.minutes)}</span>
            </li>
          ))}
        </ol>
      ) : (
        <p className="dk-muted">
          {history.inPlaydex
            ? `No sessions in the last two weeks. Playdex holds ${fmtHours(history.totalMinutes)} of history for this game.`
            : isDemo || playdexConnected
              ? "No sessions in Playdex yet."
              : "Add your Playdex API key in Settings to see this game's history."}
        </p>
      )}
      <p className="dk-note">
        {isOnPc(game) ? "Playing from here records the session to Playdex automatically." : `Synced from your ${SOURCE_LABEL[game.source]} account.`}
      </p>
    </section>
  );
}

/** Save backups for a game added to Playdex: where they stand, back up now, daily on or off. */
function SavesCard({ saves }: { saves: Saves }) {
  const [busy, setBusy] = useState(false);
  const { game } = saves;
  const running = game.backup.kind === "running";
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
      const r = await rpc<MessageView>("game.backup", { gameId: game.gameId });
      showToast(r.message);
    });
  const toggleDaily = () =>
    request(async () => {
      await rpc("game.dailyBackup", { gameId: game.gameId, enabled: !game.dailyBackup });
      showToast(game.dailyBackup ? "Daily backup is off" : "Saves will be backed up daily");
    });

  return (
    <section className="dk-card dk-saves" aria-labelledby="dk-saves">
      <div className="dk-saves-head">
        <span className="dk-badge-icon">
          <Archive aria-hidden />
        </span>
        <div>
          <h2 id="dk-saves">Saves</h2>
          <p className="dk-muted">
            {saves.status ? (
              <span className="save-note">
                <SaveGlyph kind={saves.status.kind} />
                {saves.status.label}
              </span>
            ) : saves.ready ? (
              "No backups yet."
            ) : (
              "Set up Ludusavi in Settings to back up saves."
            )}
          </p>
        </div>
      </div>
      {saves.ready && (
        <>
          <div className="dk-toggle-row">
            <span>Daily backup</span>
            <button className="switch" role="switch" aria-checked={game.dailyBackup} aria-label="Daily backup" data-nav data-kind="daily-backup" onClick={toggleDaily}>
              <i aria-hidden />
            </button>
          </div>
          <button className="dk-btn dk-btn-outline" data-nav data-kind="backup" aria-busy={running} disabled={busy} onClick={backUp}>
            {running ? "Backing up…" : "Back up now"}
          </button>
        </>
      )}
    </section>
  );
}

function Details({ game }: { game: Game }) {
  const onPc = isOnPc(game);
  return (
    <section className="dk-card" aria-labelledby="dk-details">
      <div className="dk-card-head">
        <h2 id="dk-details">Details</h2>
      </div>
      {onPc ? (
        <dl className="dk-facts">
          <div>
            <dt>Source</dt>
            <dd>{SOURCE_LABEL[game.source]}</dd>
          </div>
          <div>
            <dt>On this PC</dt>
            <dd>{isInstalled(game) ? "Installed" : "Not installed"}</dd>
          </div>
          <div>
            <dt>Recorded as</dt>
            <dd className="num">{game.capture ? `${game.capture.provider} · ${game.capture.id}` : game.art ? `Steam · ${game.art}` : "—"}</dd>
          </div>
        </dl>
      ) : (
        <p className="dk-muted dk-elsewhere">
          <MonitorSmartphone aria-hidden />
          Played on {SOURCE_LABEL[game.source]}, not available on this PC.
        </p>
      )}
    </section>
  );
}
