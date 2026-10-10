import type { LoginResponse, MeResponse } from "@/lib/auth";
import { addDays, currentUtcWeek, utcDateOnly, type DateOnly } from "@/lib/dates";
import type { AccountDayStat } from "@/models/stats";

export type HomeLoginStatus = "idle" | "loading" | "success";

export interface HomeState {
  loginStatus: HomeLoginStatus;
  error: string;
  sessionEmail: string;
  isProfileLoading: boolean;
}

export const initialHomeState: HomeState = {
  loginStatus: "idle",
  error: "",
  sessionEmail: "",
  isProfileLoading: false,
};

export function isLoginDisabled(state: HomeState): boolean {
  return state.loginStatus === "loading";
}

export function profileLabel(state: HomeState): string {
  return state.sessionEmail.length > 0 ? state.sessionEmail : state.isProfileLoading ? "Loading..." : "unknown";
}

export function beginLogin(state: HomeState): HomeState {
  return { ...state, error: "", loginStatus: "loading" };
}

export function completeLogin(state: HomeState, response: LoginResponse): HomeState {
  return { ...state, sessionEmail: response.email, loginStatus: "success" };
}

export function failLogin(state: HomeState, error: string): HomeState {
  return { ...state, error, loginStatus: "idle" };
}

/** Seeds the profile from the cached session; `isProfileLoading` tells whether `/me` must be fetched. */
export function beginProfile(state: HomeState, isLoggedIn: boolean, cachedEmail: string | null): HomeState {
  const sessionEmail = isLoggedIn ? (cachedEmail ?? "") : "";
  return { ...state, sessionEmail, isProfileLoading: isLoggedIn && sessionEmail.length === 0 };
}

export function completeProfile(state: HomeState, response: MeResponse): HomeState {
  return { ...state, sessionEmail: response.email, isProfileLoading: false };
}

export function failProfile(state: HomeState): HomeState {
  return { ...state, isProfileLoading: false };
}

/** Days of history the signed-in card asks for; a streak reaching past it shows as "365+ days". */
export const homeStreakWindowDays = 365;

/** One daily breakdown covering this week and the streak window (UTC days, within the API's 366-day cap). */
export function homeStatsRequest(now: Date): string {
  const today = utcDateOnly(now);
  return `/stats/daily?from=${addDays(today, -(homeStreakWindowDays - 1))}&to=${today}`;
}

export interface HomeMiniStats {
  todaySeconds: number;
  weekSeconds: number;
  streakDays: number;
  /** The streak runs back past the requested window, so the true length is longer. */
  streakCapped: boolean;
}

/**
 * Today, this week (from Monday) and the current streak: consecutive played days ending today,
 * or ending yesterday while today has no play yet.
 */
export function homeMiniStats(days: readonly AccountDayStat[], now: Date): HomeMiniStats {
  const played = new Map<DateOnly, number>();
  for (const day of days) played.set(day.date, (played.get(day.date) ?? 0) + Math.max(0, day.totalSeconds));

  const today = utcDateOnly(now);
  const monday = currentUtcWeek(now).from;
  let weekSeconds = 0;
  for (let date = monday; date <= today; date = addDays(date, 1)) weekSeconds += played.get(date) ?? 0;

  const windowStart = addDays(today, -(homeStreakWindowDays - 1));
  let cursor = (played.get(today) ?? 0) > 0 ? today : addDays(today, -1);
  let streakDays = 0;
  while (cursor >= windowStart && (played.get(cursor) ?? 0) > 0) {
    streakDays += 1;
    cursor = addDays(cursor, -1);
  }

  return {
    todaySeconds: played.get(today) ?? 0,
    weekSeconds,
    streakDays,
    streakCapped: streakDays > 0 && cursor < windowStart,
  };
}
