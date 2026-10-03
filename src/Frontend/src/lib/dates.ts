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

export function addDays(date: DateOnly, days: number): DateOnly {
  const value = new Date(`${date}T00:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return utcDateOnly(value);
}

export function currentUtcWeek(now: Date): DateRange {
  const today = utcDateOnly(now);
  const dayOfWeek = new Date(`${today}T00:00:00Z`).getUTCDay();
  const mondayOffset = (dayOfWeek + 6) % 7;
  const monday = addDays(today, -mondayOffset);
  return { from: monday, to: addDays(monday, 6) };
}

const monthShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

export function monthLabelsSpanYears(months: readonly AccountMonthStat[]): boolean {
  return new Set(months.map((item) => item.year)).size > 1;
}

export function formatMonthLabel(month: Pick<AccountMonthStat, "year" | "month">, spansYears: boolean): string {
  const name = monthShort[month.month - 1];
  return spansYears ? `${name} ${month.year}` : name;
}
