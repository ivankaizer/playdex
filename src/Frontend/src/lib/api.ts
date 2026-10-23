import { authSession, type RefreshResponse } from "./auth";
import { getConfig } from "./config";

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, detail: string) {
    super(detail);
    this.name = "ApiError";
    this.status = status;
  }
}

export type HttpMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

export interface RequestOptions {
  method?: HttpMethod;
  /** JSON body, or a factory producing a fresh body (e.g. FormData) for each attempt. */
  body?: unknown;
  authenticated?: boolean;
  /**
   * Runs when the session is over: the API refused the access token and then the refresh token. By default the
   * browser signs out and returns to the home page; `null` leaves it to the caller, which still gets the 401.
   */
  onAuthFailure?: (() => void | Promise<void>) | null;
  signal?: AbortSignal;
}

type Outcome<T> = { value: T; error: null } | { value: null; error: ApiError };

const RefreshLockName = "playdex.refresh";

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? "GET";
  const url = buildUrl(path);
  if (!((options.authenticated ?? true) && isApiOrigin(url))) {
    const result = await sendOnce<T>(url, method, options.body, null, options.signal);
    if (result.error !== null) throw result.error;
    return result.value;
  }

  return withSession((accessToken) => sendOnce<T>(url, method, options.body, accessToken, options.signal), options);
}

/** Fetches a binary resource (e.g. an image) with the same auth/refresh semantics. */
export async function apiBlob(path: string, options: Omit<RequestOptions, "body" | "method"> = {}): Promise<Blob> {
  const url = buildUrl(path);
  return withSession(async (accessToken): Promise<Outcome<Blob>> => {
    const headers: Record<string, string> = {};
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    const response = await fetch(url, { headers, signal: options.signal });
    if (!response.ok) return { value: null, error: await createError(response) };
    return { value: await response.blob(), error: null };
  }, options);
}

/**
 * Asks the API to revoke the stored refresh token, for signing out. Best effort: signing out neither waits for it
 * nor fails because of it.
 */
export function revokeRefreshToken(): void {
  const refreshToken = authSession.getRefreshToken();
  if (!refreshToken) return;
  void fetch(buildUrl("/logout"), {
    method: "POST",
    headers: { "Content-Type": "application/json; charset=utf-8" },
    body: JSON.stringify({ refreshToken }),
    keepalive: true,
  }).catch(() => undefined);
}

/** Sends an authenticated request; on a 401 renews the session once and retries, or ends the session. */
async function withSession<T>(
  attempt: (accessToken: string | null) => Promise<Outcome<T>>,
  options: Pick<RequestOptions, "onAuthFailure" | "signal">,
): Promise<T> {
  const accessToken = authSession.getAccessToken();
  const first = await attempt(accessToken);
  if (first.error === null) return first.value;
  if (first.error.status !== 401) throw first.error;

  // Throws when the API couldn't be asked (offline, 5xx, 429): the session may well be fine, so it stays.
  if (!(await untilAborted(refreshSession(accessToken), options.signal))) {
    await endSession(options.onAuthFailure);
    throw first.error;
  }

  const retry = await attempt(authSession.getAccessToken());
  if (retry.error === null) return retry.value;
  if (retry.error.status === 401) await endSession(options.onAuthFailure);
  throw retry.error;
}

let pendingRefresh: Promise<boolean> | null = null;

/**
 * Replaces the access token the API refused. Resolves true once there is a new one, false when the API refused the
 * refresh token (the session is over), and throws when the API couldn't be asked. A refresh token works only once
 * (the API signs the session out when one is used twice), so concurrent callers share one /refresh call and tabs
 * take turns through a Web Lock.
 */
function refreshSession(refusedAccessToken: string | null): Promise<boolean> {
  pendingRefresh ??= withRefreshLock(() => refreshUnlessReplaced(refusedAccessToken)).finally(() => {
    pendingRefresh = null;
  });
  return pendingRefresh;
}

async function refreshUnlessReplaced(refusedAccessToken: string | null): Promise<boolean> {
  // Another request or tab already got a new token while this one waited.
  const current = authSession.getAccessToken();
  if (current && current !== refusedAccessToken) return true;

  const refreshToken = authSession.getRefreshToken();
  if (!refreshToken) return false;

  const response = await fetch(buildUrl("/refresh"), {
    method: "POST",
    headers: { "Content-Type": "application/json; charset=utf-8" },
    body: JSON.stringify({ refreshToken }),
  });
  if (response.status === 400 || response.status === 401) return false;
  if (!response.ok) throw await createError(response);

  const tokens = unwrapEnvelope(await response.json()) as Partial<RefreshResponse> | null;
  if (!tokens?.accessToken || !tokens.refreshToken) throw new Error("The session could not be renewed.");
  authSession.storeTokens(tokens.accessToken, tokens.refreshToken);
  return true;
}

async function withRefreshLock<T>(work: () => Promise<T>): Promise<T> {
  const locks = typeof navigator === "undefined" ? undefined : navigator.locks;
  return locks ? await locks.request(RefreshLockName, work) : await work();
}

async function endSession(onAuthFailure: RequestOptions["onAuthFailure"]): Promise<void> {
  if (onAuthFailure === null) return;
  await (onAuthFailure ?? signOut)();
}

/** The default end of a session the API refused: forget it and start again at the home page. */
function signOut(): void {
  authSession.logout();
  window.location.assign("/");
}

/** Waits for `promise`, but lets this caller's signal abort the wait without cancelling the shared work. */
function untilAborted<T>(promise: Promise<T>, signal: AbortSignal | undefined): Promise<T> {
  if (!signal) return promise;
  return new Promise<T>((resolve, reject) => {
    const abort = () => reject(new DOMException("The operation was aborted.", "AbortError"));
    if (signal.aborted) {
      abort();
      return;
    }
    signal.addEventListener("abort", abort, { once: true });
    promise.then(resolve, reject).finally(() => signal.removeEventListener("abort", abort));
  });
}

async function sendOnce<T>(
  url: string,
  method: HttpMethod,
  body: unknown,
  accessToken: string | null,
  signal: AbortSignal | undefined,
): Promise<Outcome<T>> {
  const headers: Record<string, string> = {};
  let requestBody: BodyInit | undefined;
  if (typeof body === "function") {
    requestBody = (body as () => BodyInit)();
  } else if (body !== undefined) {
    headers["Content-Type"] = "application/json; charset=utf-8";
    requestBody = JSON.stringify(body);
  }

  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;

  const response = await fetch(url, { method, headers, body: requestBody, signal });
  if (!response.ok) return { value: null, error: await createError(response) };
  if (response.status === 204) return { value: undefined as T, error: null };

  const payload = unwrapEnvelope(await response.json());
  if (payload === null || payload === undefined) {
    throw new SyntaxError("Successful response contained no JSON data.");
  }
  return { value: payload as T, error: null };
}

function unwrapEnvelope(root: unknown): unknown {
  if (root !== null && typeof root === "object" && !Array.isArray(root) && "data" in root && "error" in root) {
    return (root as { data: unknown }).data;
  }
  return root;
}

/** The API's absolute URL without a trailing slash: what the Playdex app is told to use as its server. */
export function apiBaseUrl(): string {
  const configured = getConfig().apiBase.trim();
  try {
    return new URL(configured, window.location.href).href.replace(/\/+$/, "");
  } catch {
    return configured.replace(/\/+$/, "");
  }
}

export function buildUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) return path;
  const apiBase = getConfig().apiBase.trim().replace(/\/+$/, "");
  return `${apiBase}/${path.replace(/^\/+/, "")}`;
}

function isApiOrigin(url: string): boolean {
  try {
    const target = new URL(url, window.location.href);
    const api = new URL(getConfig().apiBase.trim(), window.location.href);
    return target.origin === api.origin;
  } catch {
    return false;
  }
}

async function createError(response: Response): Promise<ApiError> {
  let detail = "Request failed.";
  try {
    const root = (await response.json()) as Record<string, unknown> | null;
    const envelopeError = root && typeof root === "object" ? root.error : null;
    const envelopeMessage = envelopeError && typeof envelopeError === "object" ? (envelopeError as Record<string, unknown>).error : null;
    if (typeof envelopeMessage === "string" && envelopeMessage.trim()) {
      detail = envelopeMessage;
    } else if (root && typeof root.detail === "string" && root.detail.trim()) {
      detail = root.detail;
    }
  } catch {
    // Non-JSON error bodies keep the generic detail.
  }
  return new ApiError(response.status, detail);
}

export function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError";
}

/** A message for people from anything a request throws (see ./errors). */
export { errorMessage } from "./errors";
