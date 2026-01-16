export type ApiErrorResponse = {
  error: string;
};

export type ApiResponse<T> =
  | { data: T; error: null }
  | { data: null; error: ApiErrorResponse };

type RequestOptions = {
  method?: string;
  body?: unknown;
  auth?: boolean;
  onAuthFailure?: () => void;
};

// @ts-ignore
const apiBaseUrl = (import.meta.env.VITE_API_URL ?? "").trim();

const buildHeaders = (auth: boolean) => {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
  };
  if (auth) {
    const token = localStorage.getItem("playdex.token");
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
  }
  return headers;
};

const buildApiUrl = (path: string) => {
  if (/^https?:\/\//i.test(path)) {
    return path;
  }
  if (!apiBaseUrl) {
    return path;
  }
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  console.log(`${apiBaseUrl}${normalizedPath}`);
  return `${apiBaseUrl}${normalizedPath}`;
};

type RefreshResponse = {
  accessToken: string;
  refreshToken?: string;
};

type RefreshResult =
  | { ok: true; token: string }
  | { ok: false; reason: "missing" | "failed" };

const refreshAccessToken = async (): Promise<RefreshResult> => {
  const refreshToken = localStorage.getItem("playdex.refresh");
  if (!refreshToken) {
    return { ok: false, reason: "missing" };
  }

  try {
    const response = await fetch(buildApiUrl("/refresh"), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
    });
    const payload = (await response.json()) as ApiResponse<RefreshResponse>;

    if (!response.ok || payload.error) {
      return { ok: false, reason: "failed" };
    }

    localStorage.setItem("playdex.token", payload.data.accessToken);
    if (payload.data.refreshToken) {
      localStorage.setItem("playdex.refresh", payload.data.refreshToken);
    }

    return { ok: true, token: payload.data.accessToken };
  } catch {
    return { ok: false, reason: "failed" };
  }
};

export const apiRequest = async <T>(
  path: string,
  { method = "GET", body, auth = true, onAuthFailure }: RequestOptions = {}
): Promise<T> => {
  const execute = async (isRetry: boolean): Promise<T> => {
    const response = await fetch(buildApiUrl(path), {
      method,
      headers: buildHeaders(auth),
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    const payload = (await response.json()) as ApiResponse<T>;

    if (!response.ok || payload.error) {
      if (response.status === 401 && auth && !isRetry) {
        const refreshed = await refreshAccessToken();
        if (refreshed.ok) {
          return execute(true);
        }
        if (onAuthFailure) {
          onAuthFailure();
        }
      }
      throw new Error(payload.error?.error ?? "Request failed.");
    }

    return payload.data;
  };

  return execute(false);
};
