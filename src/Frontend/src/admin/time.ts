// Browser-local time helpers (replace the Blazor admin's BrowserTimeZone service).

const pad = (value: number) => String(value).padStart(2, "0");

/** Parses an ISO timestamp leniently; anything unparseable is treated as "no value". */
export function parseTime(value: string | null | undefined): Date | null {
  if (!value) return null;
  const time = Date.parse(value);
  return Number.isNaN(time) ? null : new Date(time);
}

/** `yyyy-MM-dd HH:mm` in the browser's time zone, or "-" when there is no value. */
export function formatLocal(value: string | Date | null | undefined): string {
  const date = value instanceof Date ? value : parseTime(value);
  if (!date) return "-";
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** `yyyy-MM-dd HH:mm:ss` in the browser's time zone. */
export function formatLocalSeconds(value: string | Date | null | undefined): string {
  const date = value instanceof Date ? value : parseTime(value);
  if (!date) return "-";
  return `${formatLocal(date)}:${pad(date.getSeconds())}`;
}

/**
 * The browser-local calendar day containing `now`, as UTC instants. Midnights that do not exist
 * (clocks jump forward at 00:00) resolve to the first valid instant; repeated midnights resolve
 * to the first occurrence — matching the Blazor admin's BrowserTimeZone.DayRange.
 */
export function localDayRange(now: Date): { start: Date; end: Date } {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0, 0);
  const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1, 0, 0, 0, 0);
  return { start, end };
}

/** Whole hours and minutes between two instants, never negative: `3h 07m` style without padding (`3h 7m`). */
export function formatElapsed(startedAt: string, endedAt: string | null, reference: string): string {
  const start = Date.parse(startedAt);
  const end = Date.parse(endedAt ?? reference);
  const minutes = Math.floor(Math.max(0, end - start) / 60000);
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
}

/** Shifts an ISO instant by whole hours (used for the dashboard's "last 24h" link). */
export function shiftHours(value: string, hours: number): string {
  return new Date(Date.parse(value) + hours * 3_600_000).toISOString();
}
