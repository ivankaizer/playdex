import { addDays, currentUtcWeek, getDurationSeconds, utcDateOnly } from "@/lib/dates";
import { formatDateLabel, formatDuration } from "@/lib/format";
import { encodeGameId } from "@/lib/gameHash";
import type { ManualActiveSessionResponse, ManualActiveSession, ManualPlatform, ManualPlatformsResponse } from "@/models/manual";
import type { AccountSession, AccountSessionsResponse, AccountSessionUpdateRequest } from "@/models/sessions";
import type { AccountStatsPeriod, AccountStatsRangeResponse } from "@/models/stats";
import { escapeDataString } from "./support";

export const PlatformsRequest = "/manual/platforms";
export const ActiveManualSessionRequest = "/manual/sessions/current";
export const Limit = 10;

export type SortKey = "date" | "duration";
export type SortDirection = "asc" | "desc";

export interface SessionsDeleteRequest {
  confirmation: string;
  path: string;
  method: "DELETE";
}

export function sessionsDeleteRequest(sessionId: number): SessionsDeleteRequest {
  return { confirmation: "Remove this session entry?", path: `/sessions/${sessionId}`, method: "DELETE" };
}

/** Monotonic request versions so delayed responses cannot overwrite newer ones. */
export class SessionsRequestVersions {
  private stats = 0;
  private activeManualSession = 0;

  beginStatsRequest(): number {
    return ++this.stats;
  }

  isCurrentStatsRequest(version: number): boolean {
    return version === this.stats;
  }

  beginActiveManualSessionRequest(): number {
    return ++this.activeManualSession;
  }

  isCurrentActiveManualSessionRequest(version: number): boolean {
    return version === this.activeManualSession;
  }
}

export interface SessionsRowView {
  sessionId: number;
  gameId: string;
  gamePath: string;
  title: string;
  platform: string;
  platformKey: string;
  date: string;
  /** Local calendar day of the start, "yyyy-MM-dd" (used to group the list by day). */
  dayKey: string;
  duration: string;
  durationSeconds: number;
  imageUrl: string | null;
  ignored: boolean;
  canEdit: boolean;
}

// ---------------------------------------------------------------------------
// Session edit state
// ---------------------------------------------------------------------------

export interface SessionEditState {
  session: AccountSession | null;
  startedAt: string;
  endedAt: string;
  error: string;
  isSaving: boolean;
}

export interface SessionUpdateSubmission {
  path: string;
  method: "PUT";
  body: AccountSessionUpdateRequest;
}

export const emptySessionEditState: SessionEditState = {
  session: null,
  startedAt: "",
  endedAt: "",
  error: "",
  isSaving: false,
};

export function isEditOpen(state: SessionEditState): boolean {
  return state.session !== null;
}

/** Returns the opened state, or null when the session is still active (TryOpen == false). */
export function tryOpenEdit(state: SessionEditState, session: AccountSession): SessionEditState | null {
  if (session.endedAt === null) return null;
  return {
    ...state,
    session,
    startedAt: formatLocalDateTimeInput(new Date(session.startedAt)),
    endedAt: formatLocalDateTimeInput(new Date(session.endedAt)),
  };
}

export function beginEditSave(state: SessionEditState): SessionEditState {
  return { ...state, isSaving: true, error: "" };
}

export function failEditSave(state: SessionEditState, error: string): SessionEditState {
  return { ...state, isSaving: false, error };
}

export function completeEditSave(): SessionEditState {
  return emptySessionEditState;
}

export const cancelEdit = completeEditSave;

export function setEditStartedAt(state: SessionEditState, value: string): SessionEditState {
  return { ...state, startedAt: value, error: "" };
}

export function setEditEndedAt(state: SessionEditState, value: string): SessionEditState {
  return { ...state, endedAt: value, error: "" };
}

export function tryBuildEditSubmission(state: SessionEditState): {
  state: SessionEditState;
  submission: SessionUpdateSubmission | null;
} {
  const startedAt = tryParseLocal(state.startedAt);
  const endedAt = tryParseLocal(state.endedAt);
  if (state.session === null || startedAt === null || endedAt === null) {
    return { state: { ...state, error: "Enter a valid start and end date and time." }, submission: null };
  }
  if (endedAt.getTime() <= startedAt.getTime()) {
    return { state: { ...state, error: "End time must be later than start time." }, submission: null };
  }
  return {
    state,
    submission: {
      path: `/sessions/${state.session.sessionId}`,
      method: "PUT",
      body: { startedAt: formatDateTimeOffset(startedAt), endedAt: formatDateTimeOffset(endedAt) },
    },
  };
}

function pad(value: number, length = 2): string {
  return String(value).padStart(length, "0");
}

/** Local "yyyy-MM-ddTHH:mm:ss". */
export function formatLocalDateTimeInput(date: Date): string {
  return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

/**
 * Strict "yyyy-MM-ddTHH:mm:ss" local parse. Rejects impossible values and times skipped by a DST
 * transition; an ambiguous (repeated) time resolves to the standard-offset instant like TimeZoneInfo.
 */
export function tryParseLocal(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})$/.exec(value);
  if (!match) return null;
  const [year, month, day, hour, minute, second] = match.slice(1).map(Number);
  if (year < 1) return null;

  const date = new Date(2000, 0, 1, 0, 0, 0, 0);
  date.setFullYear(year, month - 1, day);
  date.setHours(hour, minute, second, 0);
  const matches = (candidate: Date) =>
    candidate.getFullYear() === year &&
    candidate.getMonth() === month - 1 &&
    candidate.getDate() === day &&
    candidate.getHours() === hour &&
    candidate.getMinutes() === minute &&
    candidate.getSeconds() === second;
  if (!matches(date)) return null;

  const later = new Date(date.getTime() + 3_600_000);
  return matches(later) ? later : date;
}

/** System.Text.Json DateTimeOffset form with the local offset, e.g. "2026-08-04T18:30:15+02:00". */
export function formatDateTimeOffset(date: Date): string {
  const offsetMinutes = -date.getTimezoneOffset();
  const sign = offsetMinutes < 0 ? "-" : "+";
  const absolute = Math.abs(offsetMinutes);
  return `${formatLocalDateTimeInput(date)}${sign}${pad(Math.floor(absolute / 60))}:${pad(absolute % 60)}`;
}

// ---------------------------------------------------------------------------
// Sessions list state
// ---------------------------------------------------------------------------

export interface SessionsState {
  offset: number;
  preset: string;
  title: string;
  titleVersion: number;
  debouncedTitle: string;
  platform: string;
  from: string;
  to: string;
  sortKey: string;
  sortDirection: SortDirection;
  rows: readonly AccountSession[];
  total: number;
  isListLoading: boolean;
  listError: string;
  stats: AccountStatsPeriod | null;
  isStatsLoading: boolean;
  statsError: string;
  refreshVersion: number;
  deletingSessionIds: ReadonlySet<number>;
  platforms: readonly ManualPlatform[];
  activeManualSession: ManualActiveSession | null;
}

export function createSessionsState(now: Date): SessionsState {
  const range = currentUtcWeek(now);
  return {
    offset: 0,
    preset: "this-week",
    title: "",
    titleVersion: 0,
    debouncedTitle: "",
    platform: "",
    from: range.from,
    to: range.to,
    sortKey: "date",
    sortDirection: "desc",
    rows: [],
    total: 0,
    isListLoading: false,
    listError: "",
    stats: null,
    isStatsLoading: false,
    statsError: "",
    refreshVersion: 0,
    deletingSessionIds: new Set(),
    platforms: [],
    activeManualSession: null,
  };
}

export const start = (state: SessionsState) => (state.total === 0 ? 0 : state.offset + 1);
export const end = (state: SessionsState) => Math.min(state.offset + Limit, state.total);
export const totalPages = (state: SessionsState) => Math.max(1, Math.ceil(state.total / Limit));
export const currentPage = (state: SessionsState) => Math.min(totalPages(state), Math.trunc(state.offset / Limit) + 1);

export function pageNumbers(state: SessionsState): number[] {
  const maxPages = 4;
  const pages = totalPages(state);
  const startPage = Math.max(1, Math.min(currentPage(state) - 1, pages - maxPages + 1));
  return Array.from({ length: Math.min(pages, maxPages) }, (_, index) => startPage + index);
}

export function setOffset(state: SessionsState, offset: number): SessionsState {
  return { ...state, offset: Math.max(0, offset) };
}

/** Sets the raw title; the new debounce version is `result.titleVersion`. */
export function setTitle(state: SessionsState, title: string): SessionsState {
  return { ...state, title, titleVersion: state.titleVersion + 1 };
}

/** Returns null when the version is stale (ApplyDebouncedTitle == false). */
export function applyDebouncedTitle(state: SessionsState, version: number): SessionsState | null {
  if (version !== state.titleVersion) return null;
  return { ...state, debouncedTitle: state.title.trim(), offset: 0 };
}

export function setFrom(state: SessionsState, from: string): SessionsState {
  return { ...state, from, preset: "custom", offset: 0 };
}

export function setTo(state: SessionsState, to: string): SessionsState {
  return { ...state, to, preset: "custom", offset: 0 };
}

export function setPlatform(state: SessionsState, platform: string | null | undefined): SessionsState {
  return { ...state, platform: platform?.trim() ?? "", offset: 0 };
}

export function togglePlatform(state: SessionsState, platform: string): SessionsState {
  return setPlatform(state, state.platform === platform ? null : platform);
}

export function applyPreset(state: SessionsState, preset: string, now: Date): SessionsState {
  const today = utcDateOnly(now);
  const year = Number(today.slice(0, 4));
  const month = Number(today.slice(5, 7));
  let from: string;
  let to: string;
  switch (preset) {
    case "today":
      from = to = today;
      break;
    case "yesterday":
      from = to = addDays(today, -1);
      break;
    case "this-week":
    case "last-week": {
      const week = currentUtcWeek(now);
      from = preset === "last-week" ? addDays(week.from, -7) : week.from;
      to = addDays(from, 6);
      break;
    }
    case "this-month":
      from = `${pad(year, 4)}-${pad(month)}-01`;
      to = utcDateOnly(new Date(Date.UTC(year, month, 0)));
      break;
    case "last-year":
      from = `${pad(year - 1, 4)}-01-01`;
      to = `${pad(year - 1, 4)}-12-31`;
      break;
    default:
      from = `${pad(year, 4)}-01-01`;
      to = `${pad(year, 4)}-12-31`;
      break;
  }
  return { ...state, from, to, preset, offset: 0 };
}

export function sortBy(state: SessionsState, key: string): SessionsState {
  if (state.sortKey === key) {
    return { ...state, sortDirection: state.sortDirection === "asc" ? "desc" : "asc", offset: 0 };
  }
  return { ...state, sortKey: key, sortDirection: "desc", offset: 0 };
}

export function beginListLoad(state: SessionsState): SessionsState {
  return { ...state, isListLoading: true, listError: "" };
}

export function failListLoad(state: SessionsState, error: string): SessionsState {
  return { ...state, rows: [], total: 0, isListLoading: false, listError: error };
}

export function completeListLoad(state: SessionsState, response: AccountSessionsResponse): SessionsState {
  return { ...state, rows: response.sessions, total: response.total, isListLoading: false };
}

export function beginStatsLoad(state: SessionsState): SessionsState {
  return { ...state, isStatsLoading: true, statsError: "" };
}

export function hasStatsRange(state: SessionsState): boolean {
  return state.from.length > 0 && state.to.length > 0;
}

/** TryBeginStatsLoad: begins loading when both bounds exist, otherwise clears the stats. */
export function tryBeginStatsLoad(state: SessionsState): { state: SessionsState; started: boolean } {
  if (!hasStatsRange(state)) {
    return { state: { ...state, stats: null, isStatsLoading: false }, started: false };
  }
  return { state: beginStatsLoad(state), started: true };
}

export function completeStatsLoad(state: SessionsState, response: AccountStatsRangeResponse): SessionsState {
  return { ...state, stats: response.stats, isStatsLoading: false };
}

export function failStatsLoad(state: SessionsState, error: string): SessionsState {
  return { ...state, stats: null, isStatsLoading: false, statsError: error };
}

export function beginDelete(state: SessionsState, sessionId: number): SessionsState {
  const deleting = new Set(state.deletingSessionIds);
  deleting.add(sessionId);
  return { ...state, deletingSessionIds: deleting, listError: "" };
}

export function completeDelete(state: SessionsState): SessionsState {
  return { ...state, refreshVersion: state.refreshVersion + 1 };
}

export function failDelete(state: SessionsState, error: string): SessionsState {
  return { ...state, listError: error };
}

export function endDelete(state: SessionsState, sessionId: number): SessionsState {
  const deleting = new Set(state.deletingSessionIds);
  deleting.delete(sessionId);
  return { ...state, deletingSessionIds: deleting };
}

export function completePlatforms(state: SessionsState, response: ManualPlatformsResponse): SessionsState {
  return { ...state, platforms: response.platforms };
}

export function completeActiveManualSession(state: SessionsState, response: ManualActiveSessionResponse): SessionsState {
  return { ...state, activeManualSession: response.session };
}

export function getRowViews(state: SessionsState, now: Date): SessionsRowView[] {
  return state.rows.map((session) => {
    const durationSeconds = getDurationSeconds(session.startedAt, session.endedAt, now);
    return {
      sessionId: session.sessionId,
      gameId: session.gameId,
      gamePath: `/games/${encodeGameId(session.gameId)}`,
      title: session.game,
      platform: session.platformName,
      platformKey: session.platformKey,
      date: formatDateLabel(session.startedAt),
      dayKey: localDayKey(new Date(session.startedAt)),
      duration: formatDuration(durationSeconds, "compact"),
      durationSeconds,
      imageUrl: session.capsuleUrl,
      ignored: session.ignored,
      canEdit: session.endedAt !== null,
    };
  });
}

/** Local "yyyy-MM-dd" for a date. */
export function localDayKey(date: Date): string {
  return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export interface SessionDayGroup {
  key: string;
  /** "TODAY" or e.g. "SATURDAY, OCT 3"; empty when the list is not grouped by day. */
  label: string;
  /** "N sessions · total duration". */
  meta: string;
  rows: SessionsRowView[];
}

const dayGroupFormat = new Intl.DateTimeFormat("en-US", { weekday: "long", month: "short", day: "numeric" });

function groupMeta(rows: readonly SessionsRowView[]): string {
  const total = rows.reduce((sum, row) => sum + row.durationSeconds, 0);
  return `${rows.length} session${rows.length === 1 ? "" : "s"} · ${formatDuration(total, "compact")}`;
}

/**
 * Groups consecutive rows by their local start day when the list is sorted by date; any other sort
 * keeps the server order in a single unlabeled group.
 */
export function groupRowViews(state: SessionsState, rows: readonly SessionsRowView[], now: Date): SessionDayGroup[] {
  if (rows.length === 0) return [];
  if (state.sortKey !== "date") {
    return [{ key: `sort-${state.sortKey}`, label: "", meta: groupMeta(rows), rows: [...rows] }];
  }
  const today = localDayKey(now);
  const groups: SessionDayGroup[] = [];
  for (const row of rows) {
    const last = groups.at(-1);
    if (last && last.key === row.dayKey) {
      last.rows.push(row);
      continue;
    }
    const [year, month, day] = row.dayKey.split("-").map(Number);
    const label = row.dayKey === today ? "TODAY" : dayGroupFormat.format(new Date(year, month - 1, day)).toUpperCase();
    groups.push({ key: row.dayKey, label, meta: "", rows: [row] });
  }
  return groups.map((group) => ({ ...group, meta: groupMeta(group.rows) }));
}

/** Switches the filter to a custom range, keeping the current bounds. */
export function selectCustomRange(state: SessionsState): SessionsState {
  return { ...state, preset: "custom" };
}

const monthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

function parseDateOnly(value: string): { year: number; month: number; day: number } {
  const [year, month, day] = value.split("-").map(Number);
  return { year, month, day };
}

function shortDate(value: string): string {
  const { year, month, day } = parseDateOnly(value);
  return `${monthNames[month - 1].slice(0, 3)} ${day}, ${year}`;
}

/** Human label for the selected range, e.g. "Sep 28 – Oct 4, 2026", "October 2026", "Today · Oct 3, 2026". */
export function rangeLabel(state: SessionsState): string {
  const { from, to, preset } = state;
  if (from.length === 0 && to.length === 0) return "All time";
  if (to.length === 0) return `From ${shortDate(from)}`;
  if (from.length === 0) return `Until ${shortDate(to)}`;
  const a = parseDateOnly(from);
  const b = parseDateOnly(to);
  if (preset === "today" || preset === "yesterday") {
    return `${preset === "today" ? "Today" : "Yesterday"} · ${shortDate(from)}`;
  }
  if (preset === "this-month") return `${monthNames[a.month - 1]} ${a.year}`;
  if (preset === "this-year" || preset === "last-year") return String(a.year);
  if (from === to) return shortDate(from);
  const startMonth = monthNames[a.month - 1].slice(0, 3);
  const endMonth = monthNames[b.month - 1].slice(0, 3);
  if (a.year !== b.year) return `${shortDate(from)} – ${shortDate(to)}`;
  if (a.month !== b.month) return `${startMonth} ${a.day} – ${endMonth} ${b.day}, ${b.year}`;
  return `${startMonth} ${a.day} – ${b.day}, ${b.year}`;
}

export function buildListRequest(state: SessionsState): string {
  const title = state.debouncedTitle.length === 0 ? "" : `&title=${encodeQuery(state.debouncedTitle)}`;
  const platform = state.platform.length === 0 ? "" : `&platform=${encodeQuery(state.platform)}`;
  const from = state.from.length === 0 ? "" : `&from=${state.from}`;
  const to = state.to.length === 0 ? "" : `&to=${state.to}`;
  return `/sessions?limit=${Limit}&offset=${state.offset}${title}${platform}${from}${to}&sort=${state.sortKey}&direction=${state.sortDirection}`;
}

export function buildStatsRequest(state: SessionsState): string | null {
  return state.from.length === 0 || state.to.length === 0
    ? null
    : `/stats?from=${state.from}&to=${state.to}${state.platform.length === 0 ? "" : `&platform=${encodeQuery(state.platform)}`}`;
}

/** URLSearchParams-style form encoding (matches the C# EncodeQuery helper). */
export function encodeQuery(value: string): string {
  return escapeDataString(value).replaceAll("%20", "+").replaceAll("~", "%7E").replaceAll("%2A", "*");
}
