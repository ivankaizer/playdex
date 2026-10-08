import type { LoginResponse, MeResponse } from "@/lib/auth";

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
