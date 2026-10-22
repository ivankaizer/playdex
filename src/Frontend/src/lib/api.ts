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
  onAuthFailure?: () => void | Promise<void>;
  signal?: AbortSignal;
}

type Outcome<T> = { value: T; error: null } | { value: null; error: ApiError };

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? "GET";
  const url = buildUrl(path);
  const useAuthentication = (options.authenticated ?? true) && isApiOrigin(url);

  const first = await sendOnce<T>(url, method, options.body, useAuthentication, options.signal);
  if (first.error === null) return first.value;
  if (first.error.status !== 401 || !useAuthentication) throw first.error;

  if (!(await tryRefresh(options.signal))) {
    if (options.onAuthFailure) await options.onAuthFailure();
    throw first.error;
  }

  const retry = await sendOnce<T>(url, method, options.body, true, options.signal);
  if (retry.error !== null) throw retry.error;
  return retry.value;
}

/** Fetches a binary resource (e.g. an image) with the same auth/refresh semantics. */
export async function apiBlob(path: string, options: Omit<RequestOptions, "body" | "method"> = {}): Promise<Blob> {
  const url = buildUrl(path);
  const attempt = async () => {
    const headers: Record<string, string> = {};
    const token = authSession.getAccessToken();
    if (token) headers.Authorization = `Bearer ${token}`;
    return fetch(url, { headers, signal: options.signal });
  };

  let response = await attempt();
  if (response.status === 401 && (await tryRefresh(options.signal))) response = await attempt();
  if (response.status === 401 && options.onAuthFailure) await options.onAuthFailure();
  if (!response.ok) throw await createError(response);
  return response.blob();
}

async function sendOnce<T>(
  url: string,
  method: HttpMethod,
  body: unknown,
  authenticated: boolean,
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

  if (authenticated) {
    const accessToken = authSession.getAccessToken();
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  }

  const response = await fetch(url, { method, headers, body: requestBody, signal });
  if (!response.ok) return { value: null, error: await createError(response) };
  if (response.status === 204) return { value: undefined as T, error: null };

  const root = (await response.json()) as unknown;
  let payload = root;
  if (root !== null && typeof root === "object" && !Array.isArray(root) && "data" in root && "error" in root) {
    payload = (root as { data: unknown }).data;
  }
  if (payload === null || payload === undefined) {
    throw new SyntaxError("Successful response contained no JSON data.");
  }
  return { value: payload as T, error: null };
}

async function tryRefresh(signal: AbortSignal | undefined): Promise<boolean> {
  const refreshToken = authSession.getRefreshToken();
  if (!refreshToken) return false;

  try {
    const response = await sendOnce<RefreshResponse>(
      buildUrl("/refresh"),
      "POST",
      { refreshToken },
      false,
      signal,
    );
    if (response.error !== null || !response.value.accessToken || !response.value.refreshToken) return false;
    authSession.storeTokens(response.value.accessToken, response.value.refreshToken);
    return true;
  } catch (error) {
    if (isAbortError(error)) throw error;
    return false;
  }
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
    const envelopeMessage =
      envelopeError && typeof envelopeError === "object" ? (envelopeError as Record<string, unknown>).error : null;
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
