import { localDateOnly } from "@/lib/dates";
import type { DraftAcceptRow, DraftRowDto, DraftRowUpdate } from "@/models/drafts";
import type { ManualMappingSearchResult } from "@/models/manual";

/** Editable copy of a draft row (port of the C# DraftRowEdit). */
export interface DraftRowEdit {
  id: string;
  revision: number;
  title: string;
  date: string;
  hours: string;
  minutes: string;
  platformKey: string;
  selection: ManualMappingSearchResult | null;
  results: readonly ManualMappingSearchResult[];
  warnings: readonly string[];
  error: string;
  isAccepted: boolean;
  acceptedSessionId: number | null;
  /** The update last confirmed by the server; used for dirty tracking. */
  saved: DraftRowUpdate;
}

const IntMax = 2_147_483_647;

/** `int.TryParse` (NumberStyles.Integer). */
function parseInt32(value: string): number | null {
  if (!/^\s*[+-]?\d+\s*$/.test(value)) return null;
  const parsed = Number(value.trim());
  return parsed >= -2_147_483_648 && parsed <= IntMax ? parsed : null;
}

/** Whole-minute duration, or null when it is not a positive whole number with minutes under 60 and at most 24 hours. */
export function draftDuration(row: Pick<DraftRowEdit, "hours" | "minutes">): number | null {
  return durationCheck(row).minutes;
}

/** The longest session the API accepts from a draft (24 hours). */
export const MaxDraftMinutes = 24 * 60;

export interface DraftDurationCheck {
  /** Whole minutes when valid, otherwise null. */
  minutes: number | null;
  /** What is wrong; empty when the duration is fine. */
  note: string;
}

export function durationCheck(row: Pick<DraftRowEdit, "hours" | "minutes">): DraftDurationCheck {
  const hours = parseInt32(row.hours);
  const minutes = parseInt32(row.minutes);
  const invalid = (note: string): DraftDurationCheck => ({ minutes: null, note });
  if (hours === null || minutes === null || hours < 0 || minutes < 0) return invalid("Enter a positive whole-minute duration.");
  if (minutes > 59) return invalid("Minutes must be between 0 and 59.");
  const total = hours * 60 + minutes;
  if (total > MaxDraftMinutes) return invalid("A session can be at most 24 hours.");
  if (total === 0) return invalid("Add how long you played.");
  return { minutes: total, note: "" };
}

export const hasGameSelection = (row: DraftRowEdit) => row.selection !== null;

/** `DateOnly.TryParseExact(value, "yyyy-MM-dd")`. */
export function isValidDate(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return false;
  const [year, month, day] = match.slice(1).map(Number);
  if (year < 1 || month < 1 || month > 12 || day < 1) return false;
  const date = new Date(Date.UTC(2000, month - 1, 1));
  date.setUTCFullYear(year, month - 1, day);
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

/** A future date waits until the day begins (the API refuses it, like a manual entry). */
export const isFutureDate = (date: string, now: Date = new Date()) => isValidDate(date) && date > localDateOnly(now);

export const hasValidDate = (row: DraftRowEdit) => isValidDate(row.date) && !isFutureDate(row.date);
export const hasValidDuration = (row: DraftRowEdit) => draftDuration(row) !== null;
export const hasValidPlatform = (row: DraftRowEdit) => row.platformKey.trim().length > 0;
export const needsPlatformReview = (row: DraftRowEdit) => !hasValidPlatform(row) || row.platformKey === "unknown";

export function corrections(row: DraftRowEdit): string {
  return [
    !hasGameSelection(row) ? "Select a verified game." : "",
    !hasValidDate(row) ? (isFutureDate(row.date) ? "The date cannot be in the future." : "Select a valid date.") : "",
    !hasValidDuration(row) ? durationCheck(row).note : "",
    !hasValidPlatform(row) ? "Select a platform." : "",
  ]
    .filter((value) => value.length > 0)
    .join(" ");
}

export const canAccept = (row: DraftRowEdit) => !row.isAccepted && corrections(row).length === 0;

/** Rows that "Accept all ready" takes. */
export const isReady = canAccept;

export function rowUpdate(row: DraftRowEdit): DraftRowUpdate {
  return {
    revision: row.revision,
    title: row.title,
    date: row.date.trim().length === 0 ? null : row.date,
    durationMinutes: draftDuration(row),
    platformKey: row.platformKey,
    gameId: row.selection?.gameId ?? null,
    igdbId: row.selection?.igdbId ?? null,
  };
}

function sameUpdate(left: DraftRowUpdate, right: DraftRowUpdate): boolean {
  return (
    left.revision === right.revision &&
    left.title === right.title &&
    left.date === right.date &&
    left.durationMinutes === right.durationMinutes &&
    left.platformKey === right.platformKey &&
    left.gameId === right.gameId &&
    left.igdbId === right.igdbId
  );
}

export const isDirty = (row: DraftRowEdit) => !sameUpdate(rowUpdate(row), row.saved);

export const acceptance = (row: DraftRowEdit): DraftAcceptRow => ({ id: row.id, revision: row.revision });

export function createDraftRowEdit(dto: DraftRowDto): DraftRowEdit {
  return applyDraftRow(
    {
      id: dto.id,
      revision: 0,
      title: "",
      date: "",
      hours: "",
      minutes: "",
      platformKey: "unknown",
      selection: null,
      results: [],
      warnings: [],
      error: "",
      isAccepted: false,
      acceptedSessionId: null,
      saved: { revision: 0, title: "", date: null, durationMinutes: null, platformKey: "unknown", gameId: null, igdbId: null },
    },
    dto,
  );
}

/** Replaces the editable fields with the server row (keeps search results, like the C# Apply). */
export function applyDraftRow(row: DraftRowEdit, dto: DraftRowDto): DraftRowEdit {
  const next: DraftRowEdit = {
    ...row,
    id: dto.id,
    revision: dto.revision,
    title: dto.title,
    date: dto.date ?? "",
    hours: dto.durationMinutes !== null ? String(Math.trunc(dto.durationMinutes / 60)) : "0",
    minutes: dto.durationMinutes !== null ? String(dto.durationMinutes % 60) : "",
    platformKey: dto.platformKey,
    selection: dto.selection,
    warnings: dto.warnings,
    isAccepted: dto.status === "accepted",
    acceptedSessionId: dto.acceptedSessionId,
    error: "",
  };
  return { ...next, saved: rowUpdate(next) };
}

/**
 * Applies the server's answer to a PATCH that sent `sent`. When the row was edited again while the
 * request was in flight, those newer edits are kept on top of the new revision (the row stays dirty
 * so the next automatic save sends them); otherwise this is `applyDraftRow`.
 */
export function applySavedDraftRow(row: DraftRowEdit, sent: DraftRowUpdate, dto: DraftRowDto): DraftRowEdit {
  const applied = applyDraftRow(row, dto);
  if (sameUpdate(rowUpdate(row), sent)) return applied;
  return { ...row, revision: dto.revision, warnings: dto.warnings, error: "", saved: applied.saved };
}

export function setDraftTitle(row: DraftRowEdit, title: string): DraftRowEdit {
  if (row.title === title) return row;
  return { ...row, title, selection: null, results: [] };
}

export function markAccepted(row: DraftRowEdit, sessionId: number): DraftRowEdit {
  return { ...row, isAccepted: true, acceptedSessionId: sessionId, error: "" };
}
