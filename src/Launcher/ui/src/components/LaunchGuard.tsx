import { useEffect } from "react";
import { cancelLaunch, confirmLaunch, launchGuard } from "../app-state";
import { gameById } from "../data/games";
import { onAction, onDir } from "../nav/input";
import { useStore } from "../nav/store";
import { Backdrop, Glyph, Hint, Logo } from "./shared";

/** RB and Up count as pressed together this close, either first: the host reports presses, not held buttons. */
const CHORD_MS = 600;

/**
 * Asks before starting a game while another one runs ({@link launchGuard}). Presses meant for a game can reach Playdex
 * behind it, so A does nothing here: RB + Up starts the game, a pair no stray press is likely to make, and B cancels.
 * The mouse gets buttons instead (hidden while a controller is in use, so A never lands on one).
 */
export function LaunchGuard() {
  const guard = useStore(launchGuard);

  useEffect(() => {
    if (!guard) return;
    let rbAt = -Infinity;
    let upAt = -Infinity;
    const offAction = onAction((a) => {
      if (a === "rb") {
        rbAt = performance.now();
        if (rbAt - upAt < CHORD_MS) confirmLaunch();
      } else if (a === "back" || a === "guide") {
        cancelLaunch();
        // Guide still goes home.
        return a === "back";
      }
      return true;
    });
    const offDir = onDir((d) => {
      if (d === "up") {
        upAt = performance.now();
        if (upAt - rbAt < CHORD_MS) confirmLaunch();
      }
      return true;
    });
    return () => {
      offAction();
      offDir();
    };
  }, [guard]);

  if (!guard) return null;
  const game = gameById(guard.gameId);
  const running = gameById(guard.runningId);
  return (
    <div
      className="launch launch-guard"
      role="alertdialog"
      aria-modal="true"
      aria-label={`Start ${game.title} while ${running.title} is running?`}
    >
      <Backdrop gameId={game.id} />
      <div className="launch-scrim" />
      <div className="launch-body">
        <Logo game={game} className="launch-logo" titleClass="launch-title" />
        <div className="launch-state">
          <span>
            {running.title} is still running. Start {game.title} too? Playdex records both.
          </span>
        </div>
        <div className="launch-guard-actions">
          <button className="btn btn-primary btn-lg" onClick={confirmLaunch}>
            Start anyway
          </button>
          <button className="btn btn-outline btn-lg" onClick={cancelLaunch}>
            Cancel
          </button>
        </div>
        <footer className="hints">
          <span className="hint">
            <Glyph name="RB" />+<Glyph name="UP" />
            Start anyway
          </span>
          <Hint glyph="B">Cancel</Hint>
        </footer>
      </div>
    </div>
  );
}
