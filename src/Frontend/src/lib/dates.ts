import type { AccountMonthStat } from "@/models/stats";

/** A date-only value in "yyyy-MM-dd" form. */
export type DateOnly = string;

export interface DateRange {
  from: DateOnly;
  to: DateOnly;
}

export function getDurationSeconds(startedAt: string | Date, endedAt: string | Date | null | undefined, now: Date): number {
  const start = new Date(startedAt).getTime();
  const end = endedAt ? new Date(endedAt).getTime() : now.getTime();
  return Math.max(0, Math.trunc((end - start) / 1000));
}

export function utcDateOnly(date: Date): DateOnly {
  return date.toISOString().slice(0, 10);
}

/** The viewer's calendar date of an instant, "yyyy-MM-dd". */
export function localDateOnly(date: Date): DateOnly {
  const pad = (value: number, length = 2) => String(value).padStart(length, "0");
  return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function addDays(date: DateOnly, days: number): DateOnly {
  const value = new Date(`${date}T00:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return utcDateOnly(value);
}

/** Monday to Sunday of the viewer's current week. */
export function currentLocalWeek(now: Date): DateRange {
  const today = localDateOnly(now);
  const dayOfWeek = new Date(`${today}T00:00:00Z`).getUTCDay();
  const mondayOffset = (dayOfWeek + 6) % 7;
  const monday = addDays(today, -mondayOffset);
  return { from: monday, to: addDays(monday, 6) };
}

/**
 * The viewer's IANA time zone (e.g. "Europe/Warsaw"). The API takes it as `tz` and counts days, months and hours
 * the way this page shows them; it falls back to UTC for a zone it does not know.
 */
export function clientTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
  } catch {
    return "UTC";
  }
}

/** An API path with the viewer's time zone appended as `tz`. */
export function withTimeZone(path: string, timeZone: string = clientTimeZone()): string {
  return `${path}${path.includes("?") ? "&" : "?"}tz=${encodeURIComponent(timeZone)}`;
}

const monthShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

export function monthLabelsSpanYears(months: readonly AccountMonthStat[]): boolean {
  return new Set(months.map((item) => item.year)).size > 1;
}

export function formatMonthLabel(month: Pick<AccountMonthStat, "year" | "month">, spansYears: boolean): string {
  const name = monthShort[month.month - 1];
  return spansYears ? `${name} ${month.year}` : name;
}

/** "Mar 3, 2025" for a "yyyy-MM-dd" date, read as the calendar date it names (never shifted by a time zone). */
export function formatDateOnlyLabel(date: DateOnly): string {
  const [year, month, day] = date.split("-").map(Number);
  if (!year || !month || !day || month < 1 || month > 12) return date;
  return `${monthShort[month - 1]} ${day}, ${year}`;
}
