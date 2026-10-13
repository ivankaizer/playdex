import { CircleCheck, CircleHelp, Clock, LoaderCircle, TriangleAlert } from "lucide-react";
import { daysAgo } from "../data/playdex";
import { desktopState, type BackupKind, type DesktopGame } from "../host/desktop";
import { useStore } from "../nav/store";

/*
 * Ludusavi save backups, as the Wrapper showed them: games added to Playdex can back up their saves daily (Manage, or
 * the game's page). The game page, Home's lead tile and the Library covers show where a game's backups stand.
 */

export interface SaveStatus {
  kind: BackupKind;
  /** Short, for a tile: "Saves backed up today 08:51". */
  label: string;
}

const time = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" });

/** "today 08:51", "yesterday", "3 days ago". */
function backedUpWhen(ms: number) {
  const days = daysAgo(new Date(ms).toISOString());
  return days === 0 ? `today ${time.format(ms)}` : days === 1 ? "yesterday" : `${days} days ago`;
}

function statusOf(game: DesktopGame): SaveStatus | null {
  const { kind, lastSuccessAt } = game.backup;
  switch (kind) {
    case "running":
      return { kind, label: "Backing up saves…" };
    case "failed":
      return { kind, label: "Save backup failed" };
    case "unsupported":
      return { kind, label: "Ludusavi doesn't support this game" };
    case "due":
      return { kind, label: "Save backup due" };
    case "success":
      return { kind, label: lastSuccessAt ? `Saves backed up ${backedUpWhen(lastSuccessAt)}` : "Saves backed up" };
    default:
      return null;
  }
}

export interface Saves {
  game: DesktopGame;
  /** Ludusavi's path is set in Settings; without it nothing is backed up. */
  ready: boolean;
  /** Null when there is nothing to say: no backups yet and daily backup off, or Ludusavi not set up. */
  status: SaveStatus | null;
}

/** Save backups of a library game, when it was added to Playdex (and the desktop app's state has arrived). */
export function useSaves(libraryId: string): Saves | null {
  const state = useStore(desktopState);
  const game = state?.games.find((g) => g.libraryId === libraryId);
  if (!state || !game) return null;
  const ready = !!state.settings.ludusaviPath;
  return { game, ready, status: ready ? statusOf(game) : null };
}

const GLYPH = { success: CircleCheck, due: Clock, running: LoaderCircle, failed: TriangleAlert, unsupported: CircleHelp };

export function SaveGlyph({ kind }: { kind: BackupKind }) {
  const Icon = kind === "off" ? Clock : GLYPH[kind];
  return <Icon className="save-glyph" data-kind={kind} aria-hidden />;
}
