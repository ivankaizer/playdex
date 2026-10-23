import { withTimeZone } from "@/lib/dates";
import type { RecapResponse, RecapYearsResponse } from "./recapModels";

export interface RecapTab {
  key: string;
  label: string;
  from: string | null;
  to: string | null;
}

export interface RecapState {
  years: number[];
  yearsLoaded: boolean;
  activeTab: string;
  hasSearchRange: boolean;
  customFrom: string;
  customTo: string;
  recap: RecapResponse | null;
  isLoading: boolean;
  error: string;
  defaultLoaded: boolean;
}

/** A transition that also yields a value (typically the API path to load next). */
export type RecapStep<R> = readonly [RecapState, R];

export const initialRecapState: RecapState = {
  years: [],
  yearsLoaded: false,
  activeTab: "all-time",
  hasSearchRange: false,
  customFrom: "",
  customTo: "",
  recap: null,
  isLoading: false,
  error: "",
  defaultLoaded: false,
};

export function recapTabs(state: RecapState): RecapTab[] {
  return [
    { key: "all-time", label: "All time", from: null, to: null },
    ...state.years.map((year) => ({
      key: `year-${year}`,
      label: String(year),
      from: `${pad4(year)}-01-01`,
      to: `${pad4(year)}-12-31`,
    })),
    { key: "custom", label: "Custom", from: null, to: null },
  ];
}

/** Years are the viewer's calendar years, like the tabs that request them. */
export function recapYearsRequest(): string {
  return withTimeZone("/recap/years");
}

/** The all-time recap, with days, weekdays and hours in the viewer's time zone. */
export function allTimeRequest(): string {
  return withTimeZone("/recap");
}

export function beginYearsLoad(state: RecapState): RecapState {
  return { ...state, yearsLoaded: false };
}

export function completeYears(state: RecapState, response: RecapYearsResponse): RecapState {
  return { ...state, years: response.years, yearsLoaded: true };
}

export function failYears(state: RecapState, error: string): RecapState {
  return { ...state, years: [], yearsLoaded: true, error };
}

export function takeDefaultRequest(state: RecapState): RecapStep<string | null> {
  if (!state.yearsLoaded || state.hasSearchRange || state.defaultLoaded) return [state, null];
  return [{ ...state, activeTab: "all-time", defaultLoaded: true }, allTimeRequest()];
}

export function applyLocationQuery(state: RecapState, query: string): RecapStep<string | null> {
  // URLSearchParams.get returns the first value, like the Blazor parser's TryAdd.
  const values = new URLSearchParams(query);
  const from = values.get("from");
  const to = values.get("to");
  if (from && to) {
    return [
      { ...state, hasSearchRange: true, customFrom: from, customTo: to, activeTab: "custom" },
      buildRangeRequest(from, to),
    ];
  }
  return [{ ...state, hasSearchRange: false, defaultLoaded: false }, null];
}

export function selectTab(state: RecapState, key: string): RecapStep<string | null> {
  const next = { ...state, activeTab: key };
  if (key === "all-time") return [next, allTimeRequest()];
  if (key === "custom") {
    return [next, state.customFrom.length > 0 && state.customTo.length > 0 ? buildRangeRequest(state.customFrom, state.customTo) : null];
  }

  const tab = recapTabs(state).find((item) => item.key === key);
  return [next, tab?.from && tab.to ? buildRangeRequest(tab.from, tab.to) : null];
}

export function setCustomFrom(state: RecapState, from: string): RecapState {
  return { ...state, customFrom: from };
}

export function setCustomTo(state: RecapState, to: string): RecapState {
  return { ...state, customTo: to };
}

/** The page URL for the custom range (the API request adds the time zone). */
export function buildCustomNavigation(state: RecapState): string | null {
  return state.customFrom.length > 0 && state.customTo.length > 0
    ? rangePath(state.customFrom, state.customTo)
    : null;
}

export function beginLoad(state: RecapState): RecapState {
  return { ...state, isLoading: true, error: "" };
}

export function completeLoad(state: RecapState, recap: RecapResponse): RecapState {
  return { ...state, recap, isLoading: false };
}

export function failLoad(state: RecapState, error: string): RecapState {
  return { ...state, recap: null, isLoading: false, error };
}

/** The recap of an inclusive range of the viewer's calendar days. */
function buildRangeRequest(from: string, to: string): string {
  return withTimeZone(rangePath(from, to));
}

function rangePath(from: string, to: string): string {
  return `/recap?from=${escapeDataString(from)}&to=${escapeDataString(to)}`;
}

/** Equivalent of .NET Uri.EscapeDataString (RFC 3986: only unreserved characters stay literal). */
function escapeDataString(value: string): string {
  return encodeURIComponent(value).replace(/[!'()*]/g, (c) => `%${c.charCodeAt(0).toString(16).toUpperCase()}`);
}

function pad4(value: number): string {
  return value < 0 ? `-${String(-value).padStart(4, "0")}` : String(value).padStart(4, "0");
}

/**
 * Mirrors RecapInitialCustomFlow: when the page opens on a custom range, the range is
 * loaded straight away while the years load; once they settle, the all-time fallback
 * loads only if the range was lost in the meantime.
 */
export async function runInitialCustomFlow(
  customRequest: string,
  loadYears: () => Promise<void>,
  takeDefault: () => string | null,
  loadRecap: (path: string) => Promise<void>,
): Promise<void> {
  const yearsAndFallback = (async () => {
    await loadYears();
    const fallbackRequest = takeDefault();
    if (fallbackRequest !== null) await loadRecap(fallbackRequest);
  })();
  const custom = loadRecap(customRequest);
  await Promise.all([yearsAndFallback, custom]);
}
