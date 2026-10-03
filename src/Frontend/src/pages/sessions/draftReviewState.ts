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

export function draftDuration(row: Pick<DraftRowEdit, "hours" | "minutes">): number | null {
  const hours = parseInt32(row.hours);
  const minutes = parseInt32(row.minutes);
  if (hours === null || minutes === null || hours < 0 || minutes < 0) return null;
  const total = hours * 60 + minutes;
  return total > 0 && total <= IntMax ? total : null;
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

export const hasValidDate = (row: DraftRowEdit) => isValidDate(row.date);
export const hasValidDuration = (row: DraftRowEdit) => draftDuration(row) !== null;
export const hasValidPlatform = (row: DraftRowEdit) => row.platformKey.trim().length > 0;
export const needsPlatformReview = (row: DraftRowEdit) => !hasValidPlatform(row) || row.platformKey === "unknown";

export function corrections(row: DraftRowEdit): string {
  return [
    !hasGameSelection(row) ? "Select a verified game." : "",
    !hasValidDate(row) ? "Select a valid date." : "",
    !hasValidDuration(row) ? "Enter a positive whole-minute duration." : "",
    !hasValidPlatform(row) ? "Select a platform." : "",
  ]
    .filter((value) => value.length > 0)
    .join(" ");
}

export const canAccept = (row: DraftRowEdit) => !row.isAccepted && corrections(row).length === 0;

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

export function setDraftTitle(row: DraftRowEdit, title: string): DraftRowEdit {
  if (row.title === title) return row;
  return { ...row, title, selection: null, results: [] };
}

export function markAccepted(row: DraftRowEdit, sessionId: number): DraftRowEdit {
  return { ...row, isAccepted: true, acceptedSessionId: sessionId, error: "" };
}
