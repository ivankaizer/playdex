import { Download, LoaderCircle, Play, X } from "lucide-react";
import type { ReactNode } from "react";
import { closeArmed, forceCloseGame, launch, launching, resumeGame, showToast, useIsRunning } from "../app-state";
import { fmtMinutes, gameById, isInstalled, isOnPc, SOURCE_LABEL, weekdayInitial, type Game } from "../data/games";
import { useStore } from "../nav/store";

/**
 * The game's main action, as on its Big Picture page: Play, Resume and Close game while it runs, Starting while it
 * launches, Install when it isn't on this PC. Nothing for history from another platform. `round` is the icon-only
 * button on a card.
 */
export function PlayButton({ game, label = "Play", round = false }: { game: Game; label?: string; round?: boolean }) {
  const playing = useIsRunning(game.id);
  const starting = useStore(launching);
  const armed = useStore(closeArmed) === game.id;
  if (!isOnPc(game)) return null;

  if (playing) {
    if (round) {
      return (
        <button className="dk-round" data-nav data-kind="playing" data-game={game.id} aria-label={`Resume ${game.title}`} onClick={() => resumeGame(game.id)}>
          <Play fill="currentColor" aria-hidden />
        </button>
      );
    }
    return (
      <>
        <button className="dk-btn dk-btn-primary dk-btn-lg" data-nav data-nav-default data-kind="playing" data-game={game.id} onClick={() => resumeGame(game.id)}>
          <Play fill="currentColor" aria-hidden /> Resume
        </button>
        <button
          className="dk-btn dk-btn-outline dk-btn-lg"
          data-nav
          data-kind="close-game"
          data-game={game.id}
          data-armed={armed ? "" : undefined}
          aria-label={armed ? `Close ${game.title}? Press again to confirm` : `Close ${game.title}`}
          onClick={() => forceCloseGame(game.id)}
        >
          <X aria-hidden /> {armed ? "Close game?" : "Close game"}
        </button>
      </>
    );
  }

  if (starting === game.id) {
    return round ? (
      <span className="dk-round" aria-label={`Starting ${game.title}`}>
        <LoaderCircle className="dk-spin" aria-hidden />
      </span>
    ) : (
      <button className="dk-btn dk-btn-primary dk-btn-lg" disabled>
        <LoaderCircle className="dk-spin" aria-hidden /> Starting…
      </button>
    );
  }

  if (!isInstalled(game)) {
    const install = () => showToast(`${SOURCE_LABEL[game.source]} would open to install ${game.title}`);
    return round ? null : (
      <button className="dk-btn dk-btn-outline dk-btn-lg" data-nav data-nav-default data-kind="install" data-game={game.id} onClick={install}>
        <Download aria-hidden /> Install
      </button>
    );
  }

  // One game starts at a time; the top bar shows it, with Cancel.
  const start = () => {
    const busy = launching.get();
    if (busy) showToast(`${gameById(busy).title} is still starting`);
    else launch(game.id);
  };
  return round ? (
    <button className="dk-round" data-nav data-kind="game" data-game={game.id} aria-label={`Play ${game.title}`} onClick={start}>
      <Play fill="currentColor" aria-hidden />
    </button>
  ) : (
    <button className="dk-btn dk-btn-primary dk-btn-lg" data-nav data-nav-default data-kind="game" data-game={game.id} onClick={start}>
      <Play fill="currentColor" aria-hidden /> {label}
    </button>
  );
}

/** A spaced-caps label over a value. */
export function Stat({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="dk-stat">
      <span className="dk-label">{label}</span>
      <span className="dk-stat-value num">{children}</span>
    </div>
  );
}

/** Minutes per day, oldest first (the last is today), as bars with weekday initials under every seventh. */
export function DayBars({ minutes, label }: { minutes: number[]; label: string }) {
  const max = Math.max(...minutes, 1);
  const last = minutes.length - 1;
  const total = minutes.reduce((t, m) => t + m, 0);
  return (
    <div className="dk-days" role="img" aria-label={`${label}: ${total ? fmtMinutes(total) : "nothing"} in total, ${fmtMinutes(minutes[last])} today`}>
      {minutes.map((m, i) => (
        <div key={i} className="dk-day" data-today={i === last ? "" : undefined} title={m ? fmtMinutes(m) : "Not played"}>
          <div className="dk-day-bar">
            <i style={{ transform: `scaleY(${m ? Math.max(0.04, m / max) : 0})` }} />
          </div>
          <span aria-hidden>{weekdayInitial(last - i)}</span>
        </div>
      ))}
    </div>
  );
}
