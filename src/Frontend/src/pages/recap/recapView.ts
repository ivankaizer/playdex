import { formatDateOnlyLabel } from "@/lib/dates";
import { formatCountNoun, formatDuration, formatFixedNumber, formatHours } from "@/lib/format";
import type { RecapGameStat, RecapResponse, WeekdayStat } from "./recapModels";

export interface ChartPoint {
  x: number;
  y: number;
}

export interface RecapWeekdayChart {
  width: number;
  height: number;
  linePoints: string;
  areaPath: string;
  points: ChartPoint[];
}

export interface LabeledValue {
  label: string;
  value: string;
}

const MonthShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/**
 * Formats like .NET `double.ToString("0.###############", InvariantCulture)`:
 * 15 significant digits, at most 15 decimals, fixed-point notation.
 */
export function formatNumber(value: number): string {
  if (Number.isNaN(value)) return "NaN";
  if (!Number.isFinite(value)) return value > 0 ? "Infinity" : "-Infinity";
  const rounded = Number(value.toPrecision(15));
  if (rounded === 0) return "0";
  const magnitude = Math.abs(rounded);
  if (magnitude >= 1e21) return BigInt(rounded).toString();
  if (magnitude >= 1e-6) return String(rounded);
  const fixed = rounded.toFixed(15).replace(/\.?0+$/, "");
  return fixed === "-0" ? "-0" : fixed;
}

export function buildWeekdayChart(stats: readonly WeekdayStat[]): RecapWeekdayChart {
  const width = 240;
  const height = 80;
  const padding = 8;
  if (stats.length === 0) return { width, height, linePoints: "", areaPath: "", points: [] };

  const maximum = Math.max(1, ...stats.map((item) => item.sessionCount));
  const step = (width - padding * 2) / Math.max(1, stats.length - 1);
  const points = stats.map((item, index) => ({
    x: padding + index * step,
    y: height - padding - (item.sessionCount / maximum) * (height - padding * 2),
  }));
  const line = points.map((point) => `${formatNumber(point.x)},${formatNumber(point.y)}`).join(" ");
  const area = `M ${formatNumber(padding)},${formatNumber(height - padding)} L ${line} L ${formatNumber(width - padding)},${formatNumber(height - padding)} Z`;
  return { width, height, linePoints: line, areaPath: area, points };
}

/** C# `(int)Math.Floor(value + 0.5)`. */
export function roundPercent(value: number): number {
  return Math.trunc(Math.floor(value + 0.5)) || 0;
}

export function formatAverage(value: number, decimals = 1): string {
  return formatFixedNumber(value, decimals);
}

/**
 * A "yyyy-MM-dd" date as "MMM d, yyyy". The date already is a calendar day, so it is not read as UTC midnight and
 * shifted into local time (which showed the day before west of UTC).
 */
export function formatRecapDate(date: string): string {
  return formatDateOnlyLabel(date);
}

/** "yyyy-MM" → "MMM yyyy"; anything unparseable is returned unchanged. */
export function formatRecapMonthLabel(monthKey: string): string {
  const parts = monthKey.split("-");
  if (parts.length !== 2 || !/^\d+$/.test(parts[0]) || !/^\d+$/.test(parts[1])) return monthKey;
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  if (year === 0 || year > 9999 || month < 1 || month > 12) return monthKey;
  return `${MonthShort[month - 1]} ${String(year).padStart(4, "0")}`;
}

export function rangeLabel(recap: RecapResponse | null): string {
  const from = recap?.range.from;
  const to = recap?.range.to;
  return !from || !to ? "All time" : `${formatRecapDate(from)} — ${formatRecapDate(to)}`;
}

export function volumeStats(recap: RecapResponse): LabeledValue[] {
  return [
    { label: "Total sessions", value: String(recap.totalSessions) },
    { label: "Total time", value: formatHours(recap.totalSeconds, 1) },
  ];
}

export function sessionLengthStats(recap: RecapResponse): LabeledValue[] {
  return [
    { label: "Avg session length", value: formatDuration(recap.averageSessionSeconds, "full") },
    { label: "Median session length", value: formatDuration(recap.medianSessionSeconds, "full") },
    { label: "Longest session", value: formatDuration(recap.longestSessionSeconds, "full") },
    { label: "Shortest session", value: formatDuration(recap.shortestSessionSeconds, "full") },
  ];
}

export function cadenceStats(recap: RecapResponse): LabeledValue[] {
  const day = recap.mostActiveDay;
  const month = recap.mostActiveMonth;
  return [
    { label: "Avg sessions / day", value: formatAverage(recap.averageSessionsPerDay) },
    { label: "Avg sessions / week", value: formatAverage(recap.averageSessionsPerWeek) },
    { label: "Avg sessions / month", value: formatAverage(recap.averageSessionsPerMonth) },
    { label: "Most active day", value: day ? `${formatRecapDate(day.date)} · ${day.sessionCount}` : "—" },
    { label: "Most active month", value: month ? `${formatRecapMonthLabel(month.month)} · ${month.sessionCount}` : "—" },
    { label: "Longest streak", value: `${recap.longestStreakDays} days` },
  ];
}

export function gameMixStats(recap: RecapResponse): LabeledValue[] {
  const binged = recap.mostBingedGame;
  return [
    { label: "Unique games played", value: String(recap.uniqueGames) },
    { label: "New games tried", value: String(recap.newGamesTried) },
    { label: "Games abandoned", value: String(recap.abandonedGames) },
    { label: "Return rate games", value: String(recap.returnRateGames) },
    { label: "Top 3 time share", value: `${roundPercent(recap.timeOnTop3Percent)}%` },
    { label: "Most binged game", value: binged ? `${binged.title} · ${binged.sessionCount}` : "—" },
  ];
}

export function gameHighlightStats(recap: RecapResponse): LabeledValue[] {
  const sessions = recap.topGameBySessions;
  const time = recap.topGameByTime;
  const average = recap.topGameByAverageSession;
  const longest = recap.gameWithLongestSession;
  return [
    { label: "Top game by sessions", value: sessions ? `${sessions.title} · ${sessions.sessionCount}` : "—" },
    { label: "Top game by time", value: time ? `${time.title} · ${formatHours(time.totalSeconds, 1)}` : "—" },
    {
      label: "Top game by avg session",
      value: average ? `${average.title} · ${formatDuration(average.averageSessionSeconds, "full")}` : "—",
    },
    {
      label: "Longest session game",
      value: longest ? `${longest.title} · ${formatDuration(longest.sessionSeconds, "full")}` : "—",
    },
  ];
}

/** Rows of the full library shown before "Show all". */
export const LibraryPreviewCount = 20;

/** A recap with nothing logged in it (a brand-new account on "All time"). */
export function isRecapEmpty(recap: RecapResponse): boolean {
  return recap.totalSessions <= 0 && recap.gamesTable.length === 0;
}

/** Only games with playtime rank, longest first, at most five. */
export function topGames(recap: RecapResponse): RecapGameStat[] {
  return recap.topGamesByTime
    .filter((game) => game.totalSeconds > 0)
    .sort((a, b) => b.totalSeconds - a.totalSeconds)
    .slice(0, 5);
}

export function topGamesTitle(count: number): string {
  return count === 1 ? "Top game" : `Top ${count} games`;
}

/** The three hero fact tiles; all time shows the longest streak, a period its average session. */
export function heroFacts(recap: RecapResponse, allTime: boolean): LabeledValue[] {
  return [
    { label: "Top title", value: recap.topGameByTime?.title ?? "—" },
    {
      label: "Most active month",
      value: recap.mostActiveMonth ? formatRecapMonthLabel(recap.mostActiveMonth.month) : "—",
    },
    allTime
      ? { label: "Longest streak", value: formatCountNoun(recap.longestStreakDays, "day") }
      : { label: "Avg. session", value: formatDuration(recap.averageSessionSeconds, "full") },
  ];
}

/** Font size for the TOP TITLE tile: shorter titles keep the 20px figure, long ones step down. */
export function titleSizeClass(title: string): string {
  const length = Array.from(title).length;
  if (length <= 16) return "text-xl";
  if (length <= 28) return "text-[17px] leading-snug";
  return "text-[15px] leading-snug";
}

const averageFormat = new Intl.NumberFormat("en-US", { maximumFractionDigits: 1 });

/** Average sessions per day/week, grouped with one decimal ("1,000.5"). */
export function formatSessionsAverage(value: number): string {
  return averageFormat.format(Number.isFinite(value) ? value : 0);
}
