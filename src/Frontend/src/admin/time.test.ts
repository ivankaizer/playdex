import { afterEach, describe, expect, it } from "vitest";
import { formatElapsed, formatLocal, formatLocalSeconds, localDayRange, parseTime, shiftHours } from "./time";

const originalTz = process.env.TZ;

function withZone(zone: string) {
  process.env.TZ = zone;
}

afterEach(() => {
  process.env.TZ = originalTz;
});

describe("localDayRange", () => {
  // Same cases as the Blazor admin's BrowserTimeZone tests.
  it.each([
    ["Europe/Warsaw", "2026-03-29T12:00:00Z", "2026-03-28T23:00:00.000Z", 23],
    ["Europe/Warsaw", "2026-10-25T12:00:00Z", "2026-10-24T22:00:00.000Z", 25],
    ["Africa/Cairo", "2026-04-23T12:00:00Z", "2026-04-22T22:00:00.000Z", 24],
    ["Africa/Cairo", "2026-04-24T12:00:00Z", "2026-04-23T22:00:00.000Z", 23],
    ["Africa/Cairo", "2026-10-30T12:00:00Z", "2026-10-29T22:00:00.000Z", 24],
    ["Africa/Cairo", "2026-10-29T12:00:00Z", "2026-10-28T21:00:00.000Z", 25],
    ["America/Havana", "2026-11-01T12:00:00Z", "2026-11-01T04:00:00.000Z", 25],
    ["UTC", "2026-09-16T23:59:00Z", "2026-09-16T00:00:00.000Z", 24],
  ])("uses local midnights in %s for %s", (zone, now, start, hours) => {
    withZone(zone);
    const range = localDayRange(new Date(now));
    expect(range.start.toISOString()).toBe(start);
    expect((range.end.getTime() - range.start.getTime()) / 3_600_000).toBe(hours);
  });
});

describe("formatting", () => {
  it("formats instants in the browser zone", () => {
    withZone("Europe/Warsaw");
    expect(formatLocal("2026-09-16T10:05:09Z")).toBe("2026-09-16 12:05");
    expect(formatLocalSeconds("2026-09-16T10:05:09Z")).toBe("2026-09-16 12:05:09");
  });

  it("renders missing or invalid values as a dash", () => {
    expect(formatLocal(null)).toBe("-");
    expect(formatLocal("not a date")).toBe("-");
    expect(parseTime("")).toBeNull();
  });

  it("formats elapsed time against the reference for live sessions and never goes negative", () => {
    expect(formatElapsed("2026-09-16T10:00:00Z", null, "2026-09-16T13:07:59Z")).toBe("3h 7m");
    expect(formatElapsed("2026-09-16T10:00:00Z", "2026-09-16T10:45:00Z", "2026-09-17T00:00:00Z")).toBe("0h 45m");
    expect(formatElapsed("2026-09-16T10:00:00Z", null, "2026-09-16T09:00:00Z")).toBe("0h 0m");
  });

  it("shifts instants by hours", () => {
    expect(shiftHours("2026-09-16T12:00:00.000Z", -24)).toBe("2026-09-15T12:00:00.000Z");
  });
});
