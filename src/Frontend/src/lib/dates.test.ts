import { describe, expect, it } from "vitest";
import { clientTimeZone, currentLocalWeek, formatDateOnlyLabel, localDateOnly, withTimeZone } from "./dates";

describe("dates", () => {
  it("reads the viewer's calendar date, not the UTC one", () => {
    expect(localDateOnly(new Date(2026, 6, 14, 23, 30))).toBe("2026-07-14");
    expect(localDateOnly(new Date(2026, 0, 1, 0, 5))).toBe("2026-01-01");
  });

  it("runs the week from the viewer's Monday to Sunday", () => {
    // Sunday late evening and Monday just after midnight, locally.
    expect(currentLocalWeek(new Date(2026, 6, 19, 23, 59))).toEqual({ from: "2026-07-13", to: "2026-07-19" });
    expect(currentLocalWeek(new Date(2026, 6, 20, 0, 1))).toEqual({ from: "2026-07-20", to: "2026-07-26" });
  });

  it("appends the time zone as a query parameter", () => {
    expect(withTimeZone("/stats?from=2026-07-13&to=2026-07-19", "America/Los_Angeles")).toBe(
      "/stats?from=2026-07-13&to=2026-07-19&tz=America%2FLos_Angeles",
    );
    expect(withTimeZone("/recap", "Asia/Tokyo")).toBe("/recap?tz=Asia%2FTokyo");
    expect(withTimeZone("/recap")).toBe(`/recap?tz=${encodeURIComponent(clientTimeZone())}`);
    expect(clientTimeZone().length).toBeGreaterThan(0);
  });

  it("labels a date-only value as the day it names in every time zone", () => {
    expect(formatDateOnlyLabel("2026-03-01")).toBe("Mar 1, 2026");
    expect(formatDateOnlyLabel("2025-12-31")).toBe("Dec 31, 2025");
    expect(formatDateOnlyLabel("not a date")).toBe("not a date");
  });
});
