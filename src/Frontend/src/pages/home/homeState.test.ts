import { describe, expect, it } from "vitest";
import { clientTimeZone } from "@/lib/dates";
import {
  beginLogin,
  beginProfile,
  completeLogin,
  completeProfile,
  failLogin,
  homeMiniStats,
  homeStatsRequest,
  initialHomeState,
  isLoginDisabled,
  profileLabel,
} from "./homeState";

const tz = `tz=${encodeURIComponent(clientTimeZone())}`;

describe("homeState", () => {
  it("disables login only while loading and keeps the error message", () => {
    let state = beginLogin(initialHomeState);
    expect(state.loginStatus).toBe("loading");
    expect(isLoginDisabled(state)).toBe(true);
    expect(state.error).toBe("");

    state = failLogin(state, "Invalid credentials.");
    expect(state.loginStatus).toBe("idle");
    expect(isLoginDisabled(state)).toBe(false);
    expect(state.error).toBe("Invalid credentials.");

    expect(beginLogin(state).error).toBe("");
  });

  it("stores the session email on successful login", () => {
    const state = completeLogin(beginLogin(initialHomeState), {
      userId: 42,
      email: "player@example.com",
      accessToken: "access",
      refreshToken: "refresh",
    });
    expect(state.loginStatus).toBe("success");
    expect(state.sessionEmail).toBe("player@example.com");
    expect(profileLabel(state)).toBe("player@example.com");
  });

  it("loads the profile only when logged in without a cached email", () => {
    let state = beginProfile(initialHomeState, false, "stale@example.com");
    expect(state.isProfileLoading).toBe(false);
    expect(state.sessionEmail).toBe("");
    expect(profileLabel(state)).toBe("unknown");

    state = beginProfile(state, true, "cached@example.com");
    expect(state.isProfileLoading).toBe(false);
    expect(profileLabel(state)).toBe("cached@example.com");

    state = beginProfile(state, true, null);
    expect(state.isProfileLoading).toBe(true);
    expect(profileLabel(state)).toBe("Loading...");

    state = completeProfile(state, { userId: 42, email: "loaded@example.com" });
    expect(state.isProfileLoading).toBe(false);
    expect(profileLabel(state)).toBe("loaded@example.com");
  });

  describe("homeMiniStats", () => {
    const now = new Date(2026, 9, 7, 12); // Wednesday noon where the tests run
    const day = (date: string, totalSeconds: number) => ({ date, totalSeconds, sessionCount: 1, platforms: [] });

    it("asks for a 365-day window ending today", () => {
      expect(homeStatsRequest(now)).toBe(`/stats/daily?from=2025-10-08&to=2026-10-07&${tz}`);
    });

    it("sums today and the week from Monday and counts the streak ending today", () => {
      const stats = homeMiniStats([day("2026-10-04", 900), day("2026-10-05", 3600), day("2026-10-06", 60), day("2026-10-07", 120)], now);
      expect(stats).toEqual({ todaySeconds: 120, weekSeconds: 3780, streakDays: 4, streakCapped: false });
    });

    it("keeps yesterday's streak alive before today's first session and reports zero for an empty history", () => {
      expect(homeMiniStats([day("2026-10-05", 60), day("2026-10-06", 60), day("2026-10-07", 0)], now).streakDays).toBe(2);
      expect(homeMiniStats([], now)).toEqual({ todaySeconds: 0, weekSeconds: 0, streakDays: 0, streakCapped: false });
    });

    it("flags a streak that runs past the window", () => {
      const days = Array.from({ length: 365 }, (_, i) => day(new Date(Date.UTC(2026, 9, 7 - i)).toISOString().slice(0, 10), 60));
      expect(homeMiniStats(days, now)).toMatchObject({ streakDays: 365, streakCapped: true });
    });
  });
});
