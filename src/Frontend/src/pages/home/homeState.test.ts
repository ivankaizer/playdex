import { describe, expect, it } from "vitest";
import {
  beginLogin,
  beginProfile,
  completeLogin,
  completeProfile,
  failLogin,
  initialHomeState,
  isLoginDisabled,
  profileLabel,
} from "./homeState";

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
});
