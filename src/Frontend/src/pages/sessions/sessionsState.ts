import { addDays, currentLocalWeek, getDurationSeconds, localDateOnly, withTimeZone } from "@/lib/dates";
import { formatCount, formatCountNoun, formatDateLabel, formatDuration } from "@/lib/format";
import { encodeGameId } from "@/lib/gameHash";
import type { ManualActiveSessionResponse, ManualActiveSession, ManualPlatform, ManualPlatformsResponse } from "@/models/manual";
import type { AccountSession, AccountSessionsResponse, AccountSessionUpdateRequest } from "@/models/sessions";
import type { AccountDayStat, AccountStatsPeriod, AccountStatsRangeResponse } from "@/models/stats";
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
  /** Duration as typed, e.g. "1h 30m". */
  duration: string;
  /** The duration text the dialog opened with; left untouched it keeps the exact original length. */
  initialDuration: string;
  initialSeconds: number;
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
  duration: "",
  initialDuration: "",
  initialSeconds: 0,
  error: "",
  isSaving: false,
};

export function isEditOpen(state: SessionEditState): boolean {
  return state.session !== null;
}

/** "1h 30m" / "45m" / "2h" for the duration field (minutes, seconds dropped). */
export function formatDurationInput(seconds: number): string {
  const minutes = Math.max(0, Math.floor(seconds / 60));
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) return `${rest}m`;
  return rest === 0 ? `${hours}h` : `${hours}h ${pad(rest)}m`;
}

/** Returns the opened state, or null when the session is still active (TryOpen == false). */
export function tryOpenEdit(state: SessionEditState, session: AccountSession): SessionEditState | null {
  if (session.endedAt === null) return null;
  const started = new Date(session.startedAt);
  const seconds = Math.max(0, Math.round((new Date(session.endedAt).getTime() - started.getTime()) / 1000));
  const duration = formatDurationInput(seconds);
  return {
    ...state,
    session,
    startedAt: formatLocalDateTimeInput(started),
    duration,
    initialDuration: duration,
    initialSeconds: seconds,
    error: "",
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

export function setEditDuration(state: SessionEditState, value: string): SessionEditState {
  return { ...state, duration: value, error: "" };
}

export interface DurationCheck {
  /** Whole minutes, or null when the text is not a valid duration. */
  minutes: number | null;
  ok: boolean;
  message: string;
}

/** The longest session the API records. */
const DayMinutes = 24 * 60;
const TooLongMessage = "A session can be at most 24 hours.";

/** Duration rules: empty and zero are rejected, minutes stay under 60, and a session lasts at most 24 hours. */
export function checkDuration(value: string): DurationCheck {
  const text = value.trim();
  if (text.length === 0) return { minutes: null, ok: false, message: "Enter how long you played, e.g. 1h 30m." };
  const match = /^(?:(\d+)\s*h)?\s*(?:(\d+)\s*m)?$/i.exec(text);
  if (!match) return { minutes: null, ok: false, message: "Use hours and minutes, e.g. 1h 30m." };
  const hours = Number(match[1] ?? 0);
  const minutes = Number(match[2] ?? 0);
  if (minutes > 59) return { minutes: null, ok: false, message: `Minutes go up to 59. Write ${Math.floor(minutes / 60) + hours}h ${pad(minutes % 60)}m instead.` };
  const total = hours * 60 + minutes;
  if (total === 0) return { minutes: 0, ok: false, message: "A session needs at least 1 minute." };
  if (total > DayMinutes) return { minutes: total, ok: false, message: TooLongMessage };
  return { minutes: total, ok: true, message: "" };
}

/** The duration check for the dialog; an untouched duration keeps its exact length, unless it is over 24 hours. */
export function editDurationCheck(state: SessionEditState): DurationCheck {
  if (state.session !== null && state.duration.trim() === state.initialDuration) {
    const minutes = Math.floor(state.initialSeconds / 60);
    return state.initialSeconds > DayMinutes * 60
      ? { minutes, ok: false, message: TooLongMessage }
      : { minutes, ok: true, message: "" };
  }
  return checkDuration(state.duration);
}

export function tryBuildEditSubmission(state: SessionEditState): {
  state: SessionEditState;
  submission: SessionUpdateSubmission | null;
} {
  const startedAt = tryParseLocal(state.startedAt);
  if (state.session === null || startedAt === null) {
    return { state: { ...state, error: "Enter a valid start date and time." }, submission: null };
  }
  const check = editDurationCheck(state);
  if (!check.ok || check.minutes === null) {
    return { state: { ...state, error: check.message }, submission: null };
  }
  const seconds = state.duration.trim() === state.initialDuration ? state.initialSeconds : check.minutes * 60;
  const endedAt = new Date(startedAt.getTime() + seconds * 1000);
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
  preset: string;
  title: string;
  titleVersion: number;
  debouncedTitle: string;
  platform: string;
  from: string;
  to: string;
  sortKey: string;
  sortDirection: SortDirection;
  /** Every loaded row; "Load more" appends the next page. */
  rows: readonly AccountSession[];
  total: number;
  isListLoading: boolean;
  isLoadingMore: boolean;
  listError: string;
  stats: AccountStatsPeriod | null;
  /** Per-day totals for short (week-sized) ranges; null for longer ranges. */
  daily: readonly AccountDayStat[] | null;
  isStatsLoading: boolean;
  statsError: string;
  refreshVersion: number;
  deletingSessionIds: ReadonlySet<number>;
  platforms: readonly ManualPlatform[];
  activeManualSession: ManualActiveSession | null;
}

export function createSessionsState(now: Date): SessionsState {
  const range = currentLocalWeek(now);
  return {
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
    isLoadingMore: false,
    listError: "",
    stats: null,
    daily: null,
    isStatsLoading: false,
    statsError: "",
    refreshVersion: 0,
    deletingSessionIds: new Set(),
    platforms: [],
    activeManualSession: null,
  };
}

/** Rows still on the server after the loaded ones. */
export const remaining = (state: SessionsState) => Math.max(0, state.total - state.rows.length);
export const hasMore = (state: SessionsState) => remaining(state) > 0;
/** Size of the next "Load more" page. */
export const nextPageSize = (state: SessionsState) => Math.min(Limit, remaining(state));

/** "Showing 10 of 1,284 sessions". */
export function showingLabel(state: SessionsState): string {
  return `Showing ${formatCount(state.rows.length)} of ${formatCountNoun(state.total, "session")}`;
}

/** Sets the raw title; the new debounce version is `result.titleVersion`. */
export function setTitle(state: SessionsState, title: string): SessionsState {
  return { ...state, title, titleVersion: state.titleVersion + 1 };
}

/** Returns null when the version is stale (ApplyDebouncedTitle == false). */
export function applyDebouncedTitle(state: SessionsState, version: number): SessionsState | null {
  if (version !== state.titleVersion) return null;
  return { ...state, debouncedTitle: state.title.trim() };
}

export function setFrom(state: SessionsState, from: string): SessionsState {
  return { ...state, from, preset: "custom" };
}

export function setTo(state: SessionsState, to: string): SessionsState {
  return { ...state, to, preset: "custom" };
}

export function setPlatform(state: SessionsState, platform: string | null | undefined): SessionsState {
  return { ...state, platform: platform?.trim() ?? "" };
}

export function togglePlatform(state: SessionsState, platform: string): SessionsState {
  return setPlatform(state, state.platform === platform ? null : platform);
}

/**
 * The platform filter's options: every platform Playdex knows (/manual/platforms) and any other the range's stats
 * mention. Not the stats alone: they are filtered by the selected platform too, so the other options would vanish
 * once one is picked. The selected platform always has an option, so the select never shows "All platforms" while a
 * filter applies.
 */
export function platformFilterOptions(state: SessionsState): Array<{ key: string; label: string }> {
  const options = new Map<string, string>();
  for (const platform of state.platforms) options.set(platform.key, platform.name);
  for (const item of state.stats?.platforms ?? []) {
    if (!options.has(item.platformKey)) options.set(item.platformKey, item.platformName);
  }
  if (state.platform.length > 0 && !options.has(state.platform)) options.set(state.platform, state.platform);
  return [...options].map(([key, label]) => ({ key, label }));
}

export function applyPreset(state: SessionsState, preset: string, now: Date): SessionsState {
  const today = localDateOnly(now);
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
      const week = currentLocalWeek(now);
      from = preset === "last-week" ? addDays(week.from, -7) : week.from;
      to = addDays(from, 6);
      break;
    }
    case "this-month":
      from = `${pad(year, 4)}-${pad(month)}-01`;
      to = localDateOnly(new Date(year, month, 0));
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
  return { ...state, from, to, preset };
}

export function sortBy(state: SessionsState, key: string): SessionsState {
  if (state.sortKey === key) {
    return { ...state, sortDirection: state.sortDirection === "asc" ? "desc" : "asc" };
  }
  return { ...state, sortKey: key, sortDirection: "desc" };
}

/**
 * How the list loads: "replace" fetches the first page (filters changed), "more" appends the next page,
 * and "refresh" reloads every row already shown (after an edit or delete) so the list keeps its length.
 */
export type ListLoadMode = "replace" | "more" | "refresh";

/** The API caps one page at 100 rows. */
export const MaxPageSize = 100;

/** The page requests for a load, in order (a refresh past 100 rows takes several). */
export function listRequestPages(state: SessionsState, mode: ListLoadMode): string[] {
  if (mode === "more") return [buildListRequest(state, state.rows.length, Limit)];
  const count = mode === "refresh" ? Math.max(Limit, state.rows.length) : Limit;
  const pages: string[] = [];
  for (let offset = 0; offset < count; offset += MaxPageSize) {
    pages.push(buildListRequest(state, offset, Math.min(MaxPageSize, count - offset)));
  }
  return pages;
}

export function beginListLoad(state: SessionsState, mode: ListLoadMode = "replace"): SessionsState {
  return mode === "more"
    ? { ...state, isLoadingMore: true, listError: "" }
    : { ...state, isListLoading: mode === "replace" || state.rows.length === 0, listError: "" };
}

export function failListLoad(state: SessionsState, error: string, mode: ListLoadMode = "replace"): SessionsState {
  // A failed "Load more" keeps the rows already shown.
  if (mode === "more") return { ...state, isLoadingMore: false, listError: error };
  return { ...state, rows: [], total: 0, isListLoading: false, isLoadingMore: false, listError: error };
}

export function completeListLoad(
  state: SessionsState,
  response: AccountSessionsResponse | { sessions: AccountSession[]; total: number },
  mode: ListLoadMode = "replace",
): SessionsState {
  if (mode !== "more") {
    return { ...state, rows: response.sessions, total: response.total, isListLoading: false, isLoadingMore: false };
  }
  // Rows can shift between pages when sessions are added meanwhile; never show one twice.
  const seen = new Set(state.rows.map((row) => row.sessionId));
  const added = response.sessions.filter((row) => !seen.has(row.sessionId));
  return { ...state, rows: [...state.rows, ...added], total: response.total, isLoadingMore: false };
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
    return { state: { ...state, stats: null, daily: null, isStatsLoading: false }, started: false };
  }
  return { state: beginStatsLoad(state), started: true };
}

export function completeStatsLoad(
  state: SessionsState,
  response: AccountStatsRangeResponse,
  daily: readonly AccountDayStat[] | null = null,
): SessionsState {
  return { ...state, stats: response.stats, daily, isStatsLoading: false };
}

export function failStatsLoad(state: SessionsState, error: string): SessionsState {
  return { ...state, stats: null, daily: null, isStatsLoading: false, statsError: error };
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
  return `${formatCountNoun(rows.length, "session")} · ${formatDuration(total, "compact")}`;
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

/** The title and platform filters, shared by the list and its stats so both count the same sessions. */
function filterQuery(state: SessionsState): string {
  const title = state.debouncedTitle.length === 0 ? "" : `&title=${encodeQuery(state.debouncedTitle)}`;
  const platform = state.platform.length === 0 ? "" : `&platform=${encodeQuery(state.platform)}`;
  return `${title}${platform}`;
}

/** The range bounds are the viewer's calendar days, so a bounded request carries the time zone. */
function rangeRequest(path: string, state: SessionsState): string {
  return state.from.length === 0 && state.to.length === 0 ? path : withTimeZone(path);
}

export function buildListRequest(state: SessionsState, offset = 0, limit = Limit): string {
  const from = state.from.length === 0 ? "" : `&from=${state.from}`;
  const to = state.to.length === 0 ? "" : `&to=${state.to}`;
  return rangeRequest(
    `/sessions?limit=${limit}&offset=${offset}${filterQuery(state)}${from}${to}&sort=${state.sortKey}&direction=${state.sortDirection}`,
    state,
  );
}

export function buildStatsRequest(state: SessionsState): string | null {
  return state.from.length === 0 || state.to.length === 0
    ? null
    : rangeRequest(`/stats?from=${state.from}&to=${state.to}${filterQuery(state)}`, state);
}

/** Longest range (in days) that gets the hours-per-day chart. */
export const MaxDailyRangeDays = 7;

/** Days in an inclusive "yyyy-MM-dd" range, or 0 when a bound is missing or reversed. */
export function rangeDays(state: SessionsState): number {
  if (!hasStatsRange(state)) return 0;
  const days = Math.round((Date.parse(`${state.to}T00:00:00Z`) - Date.parse(`${state.from}T00:00:00Z`)) / 86_400_000) + 1;
  return Number.isFinite(days) && days > 0 ? days : 0;
}

/** The per-day stats request for week-sized ranges (2 to 7 days), else null. */
export function buildDailyRequest(state: SessionsState): string | null {
  const days = rangeDays(state);
  if (days < 2 || days > MaxDailyRangeDays) return null;
  return rangeRequest(`/stats/daily?from=${state.from}&to=${state.to}${filterQuery(state)}`, state);
}

const weekdayFormat = new Intl.DateTimeFormat("en-US", { weekday: "short", timeZone: "UTC" });

export interface DayBar {
  key: string;
  /** "Mon 28". */
  label: string;
  seconds: number;
  /** After today: nothing could have been played yet. */
  upcoming: boolean;
}

/** One bar per day of the range, with days missing from the response counted as zero. */
export function dailyBars(state: SessionsState, now: Date): DayBar[] {
  if (state.daily === null) return [];
  const days = rangeDays(state);
  const today = localDateOnly(now);
  const byDate = new Map(state.daily.map((day) => [day.date, day.totalSeconds]));
  return Array.from({ length: days }, (_, index) => {
    const date = addDays(state.from, index);
    return {
      key: date,
      label: `${weekdayFormat.format(new Date(`${date}T00:00:00Z`))} ${Number(date.slice(8, 10))}`,
      seconds: byDate.get(date) ?? 0,
      upcoming: date > today,
    };
  });
}

/** URLSearchParams-style form encoding (matches the C# EncodeQuery helper). */
export function encodeQuery(value: string): string {
  return escapeDataString(value).replaceAll("%20", "+").replaceAll("~", "%7E").replaceAll("%2A", "*");
}
