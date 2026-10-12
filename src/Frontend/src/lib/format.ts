export type DurationFormat = "compact" | "full";

export function formatProviderName(platform: string): string {
  switch (platform) {
    case "steam":
      return "Steam";
    case "psn":
      return "PlayStation";
    case "xbox":
      return "Xbox";
    case "playnite":
      return "Playnite";
    case "manual":
      return "Manual";
    case "api":
      return "Playdex app";
    default:
      return platform;
  }
}

const dateLabelFormat = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric" });
const timeLabelFormat = new Intl.DateTimeFormat("en-US", { hour: "numeric", minute: "2-digit", hour12: true });

/** "MMM d, yyyy" in local time, e.g. "Oct 3, 2026". */
export function formatDateLabel(date: Date | string): string {
  return dateLabelFormat.format(toDate(date));
}

/** "h:mm tt" in local time, e.g. "3:05 PM". */
export function formatTimeLabel(date: Date | string): string {
  return timeLabelFormat.format(toDate(date)).replace(/ /g, " ");
}

/** "M/d/yyyy, h:mm:ss tt" in local time, e.g. "10/3/2026, 3:05:09 PM". */
export function formatLocalDateTime(date: Date | string): string {
  const value = toDate(date);
  const hours = value.getHours();
  const hour12 = hours % 12 === 0 ? 12 : hours % 12;
  const suffix = hours < 12 ? "AM" : "PM";
  return `${value.getMonth() + 1}/${value.getDate()}/${value.getFullYear()}, ${hour12}:${pad2(value.getMinutes())}:${pad2(value.getSeconds())} ${suffix}`;
}

export function formatDuration(seconds: number, format: DurationFormat): string {
  const safeSeconds = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(safeSeconds / 3600);
  const minutes = Math.floor((safeSeconds % 3600) / 60);

  if (hours <= 0 && minutes <= 0) return format === "compact" ? "0m" : "0 min";

  if (format === "compact") {
    return hours > 0 ? `${hours}h ${String(minutes).padStart(2, "0")}m` : `${minutes}m`;
  }

  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes} min`;
}

export function formatHours(seconds: number, decimals: number): string {
  const hours = seconds / 3600;
  if (decimals !== 0) return `${formatFixedNumber(hours, decimals)}h`;

  let value = Math.floor(hours + 0.5);
  if (Object.is(value, -0)) value = 0;
  return `${value.toFixed(0)}h`;
}

/** Equivalent to `Number(value.toFixed(decimals)).toString()`. */
export function formatFixedNumber(value: number, decimals: number): string {
  if (!Number.isFinite(value)) return "0";
  if (value === 0) return "0";
  const fixed = Number(value.toFixed(decimals));
  return fixed === 0 ? "0" : String(fixed);
}

/** UTC "yyyy-MM-dd" for a date. */
export function formatUtcDateInput(date: Date | string): string {
  return toDate(date).toISOString().slice(0, 10);
}

/** Invariant shortest round-trip number formatting (matches C# double.ToString(InvariantCulture)). */
export function invariant(value: number): string {
  return String(value);
}

function toDate(date: Date | string): Date {
  return typeof date === "string" ? new Date(date) : date;
}

function pad2(value: number): string {
  return String(value).padStart(2, "0");
}

const integerFormat = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
const pluralRules = new Intl.PluralRules("en-US");

/** Grouped whole number, e.g. "12,480". */
export function formatCount(value: number): string {
  return integerFormat.format(Number.isFinite(value) ? value : 0);
}

/** "1 session" / "12,480 sessions"; `plural` defaults to `${one}s`. */
export function formatCountNoun(value: number, one: string, plural = `${one}s`): string {
  const safe = Number.isFinite(value) ? value : 0;
  return `${formatCount(safe)} ${pluralRules.select(safe) === "one" ? one : plural}`;
}

/** Hours with one decimal, shown as "<0.1h" for a non-zero value that would round to zero. */
export function formatHoursShort(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return "0h";
  const hours = seconds / 3600;
  if (hours < 0.05) return "<0.1h";
  return `${new Intl.NumberFormat("en-US", { maximumFractionDigits: 1 }).format(hours)}h`;
}

/** Rounded share as "52%", or "<1%" for a non-zero sliver. */
export function formatPercent(value: number, total: number): string {
  if (!(total > 0) || !(value > 0)) return "0%";
  const pct = (value / total) * 100;
  return pct < 1 ? "<1%" : `${Math.round(pct)}%`;
}

/** First letter or digit grapheme of a name, uppercased ("?" when there is none). Safe for emoji and CJK. */
export function initialOf(text: string | null | undefined): string {
  const value = (text ?? "").trim();
  if (!value) return "?";
  const graphemes =
    typeof Intl.Segmenter === "function"
      ? Array.from(new Intl.Segmenter(undefined, { granularity: "grapheme" }).segment(value), (part) => part.segment)
      : Array.from(value);
  const first = graphemes.find((grapheme) => /[\p{L}\p{N}]/u.test(grapheme)) ?? graphemes[0];
  return first.toUpperCase();
}
