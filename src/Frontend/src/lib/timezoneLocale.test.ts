import { afterEach, describe, expect, it, vi } from "vitest";

// format.ts builds its Intl formatters when it is imported, so every case sets the zone (or the default locale)
// first and then imports fresh copies of the helpers.

type Format = typeof import("./format");
type Dates = typeof import("./dates");

const originalTz = process.env.TZ;

async function inZone(zone: string): Promise<Format & Dates> {
  process.env.TZ = zone;
  vi.resetModules();
  return { ...(await import("./format")), ...(await import("./dates")) };
}

/** Makes Intl formatters created without an explicit locale use `locale`, like a browser set to that language. */
async function withDefaultLocale(locale: string): Promise<Format> {
  const { NumberFormat, DateTimeFormat, PluralRules } = Intl;
  // Intl's members are not enumerable, so copy them by name rather than spreading.
  const stub: Record<string, unknown> = {};
  for (const name of Object.getOwnPropertyNames(Intl)) stub[name] = Reflect.get(Intl, name);
  stub.NumberFormat = function (locales?: Intl.LocalesArgument, options?: Intl.NumberFormatOptions) {
    return new NumberFormat(locales ?? locale, options);
  };
  stub.DateTimeFormat = function (locales?: Intl.LocalesArgument, options?: Intl.DateTimeFormatOptions) {
    return new DateTimeFormat(locales ?? locale, options);
  };
  stub.PluralRules = function (locales?: Intl.LocalesArgument, options?: Intl.PluralRulesOptions) {
    return new PluralRules(locales ?? locale, options);
  };
  vi.stubGlobal("Intl", stub);
  vi.resetModules();
  return import("./format");
}

afterEach(() => {
  process.env.TZ = originalTz;
  vi.unstubAllGlobals();
});

describe("local labels follow the browser's time zone", () => {
  it("shows an evening in Los Angeles on the previous calendar day", async () => {
    const f = await inZone("America/Los_Angeles");
    const instant = "2026-10-03T05:00:00Z";

    expect(f.formatDateLabel(instant)).toBe("Oct 2, 2026");
    expect(f.formatTimeLabel(instant)).toBe("10:00 PM");
    expect(f.formatLocalDateTime(instant)).toBe("10/2/2026, 10:00:00 PM");
  });

  it("shows an early morning in Tokyo on the next calendar day", async () => {
    const f = await inZone("Asia/Tokyo");
    const instant = new Date("2026-10-02T20:00:00Z");

    expect(f.formatDateLabel(instant)).toBe("Oct 3, 2026");
    expect(f.formatTimeLabel(instant)).toBe("5:00 AM");
    expect(f.formatLocalDateTime(instant)).toBe("10/3/2026, 5:00:00 AM");
  });

  it("handles half-hour offsets", async () => {
    const f = await inZone("Asia/Kolkata");

    expect(f.formatTimeLabel("2026-10-03T00:15:00Z")).toBe("5:45 AM");
    expect(f.formatLocalDateTime("2026-10-02T18:29:59Z")).toBe("10/2/2026, 11:59:59 PM");
  });

  it("uses a plain space before AM/PM, never the narrow no-break space newer ICU emits", async () => {
    const f = await inZone("UTC");

    expect(f.formatTimeLabel("2026-10-03T12:05:00Z")).toBe("12:05 PM");
    expect(f.formatTimeLabel("2026-10-03T00:05:00Z")).toBe("12:05 AM");
    expect(f.formatTimeLabel("2026-10-03T12:05:00Z")).not.toMatch(/ | /);
  });

  it("formats daylight-saving days by their local wall clock", async () => {
    const f = await inZone("Europe/Warsaw");

    // 25 October 2026: clocks go back at 03:00 CEST, so 00:30Z and 01:30Z are both "2:30 AM" / "2:30 AM".
    expect(f.formatTimeLabel("2026-10-25T00:30:00Z")).toBe("2:30 AM");
    expect(f.formatTimeLabel("2026-10-25T01:30:00Z")).toBe("2:30 AM");
    expect(f.formatDateLabel("2026-03-28T23:30:00Z")).toBe("Mar 29, 2026");
  });

  it("takes the week from the viewer's date, Monday to Sunday", async () => {
    const f = await inZone("America/Los_Angeles");

    // Sunday 4 October 23:30 in Los Angeles is already Monday 5 October in UTC, but still the viewer's Sunday.
    expect(f.localDateOnly(new Date("2026-10-05T06:30:00Z"))).toBe("2026-10-04");
    expect(f.currentLocalWeek(new Date("2026-10-05T06:30:00Z"))).toEqual({ from: "2026-09-28", to: "2026-10-04" });
    // An hour later it is Monday in Los Angeles too, and a new week.
    expect(f.currentLocalWeek(new Date("2026-10-05T07:30:00Z"))).toEqual({ from: "2026-10-05", to: "2026-10-11" });
  });
});

describe("UTC helpers ignore the browser's time zone", () => {
  it.each(["America/Los_Angeles", "Asia/Tokyo", "Pacific/Kiritimati", "Pacific/Pago_Pago"])("in %s", async (zone) => {
    const f = await inZone(zone);

    expect(f.formatUtcDateInput("2026-10-03T05:00:00Z")).toBe("2026-10-03");
    expect(f.utcDateOnly(new Date("2026-10-02T23:59:59Z"))).toBe("2026-10-02");
    expect(f.addDays("2026-10-03", 1)).toBe("2026-10-04");
    expect(f.addDays("2026-03-01", -1)).toBe("2026-02-28");
  });

  it("adds whole days across daylight-saving changes", async () => {
    const f = await inZone("America/New_York");

    expect(f.addDays("2026-03-07", 1)).toBe("2026-03-08");
    expect(f.addDays("2026-03-08", 1)).toBe("2026-03-09");
    expect(f.addDays("2026-11-01", -1)).toBe("2026-10-31");
    expect(f.addDays("2026-10-31", 2)).toBe("2026-11-02");
  });

  it("measures durations from instants, whatever the zone", async () => {
    const f = await inZone("Europe/Warsaw");

    // Across the night the clocks go back: two hours of wall-clock time are three real hours.
    expect(f.getDurationSeconds("2026-10-24T23:00:00Z", "2026-10-25T02:00:00Z", new Date())).toBe(3 * 3600);
    expect(f.getDurationSeconds("2026-10-03T10:00:00Z", null, new Date("2026-10-03T10:00:30Z"))).toBe(30);
    expect(f.getDurationSeconds("2026-10-03T10:00:00Z", "2026-10-03T09:00:00Z", new Date())).toBe(0);
  });
});

describe("labels stay English when the browser's language is not", () => {
  it.each(["de-DE", "fr-FR", "ar-EG", "hi-IN"])("with %s as the default locale", async (locale) => {
    process.env.TZ = "UTC";
    const f = await withDefaultLocale(locale);

    expect(f.formatCount(12_480)).toBe("12,480");
    expect(f.formatCountNoun(1, "session")).toBe("1 session");
    expect(f.formatCountNoun(1_284_000, "session")).toBe("1,284,000 sessions");
    expect(f.formatHoursShort(5_400)).toBe("1.5h");
    expect(f.formatHoursShort(4_500_000)).toBe("1,250h");
    expect(f.formatDateLabel("2026-10-03T12:00:00Z")).toBe("Oct 3, 2026");
    expect(f.formatTimeLabel("2026-10-03T15:05:00Z")).toBe("3:05 PM");
    expect(f.formatHours(5_400, 1)).toBe("1.5h");
    expect(f.formatFixedNumber(2.25, 1)).toBe("2.3");
    expect(f.invariant(45.5)).toBe("45.5");
    expect(f.formatPercent(1, 3)).toBe("33%");
  });

  it("the stub really changes unlabelled formatting, so the cases above would catch a missing locale", async () => {
    await withDefaultLocale("de-DE");
    expect(new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(1250.5)).toBe("1.250,5");
  });
});
