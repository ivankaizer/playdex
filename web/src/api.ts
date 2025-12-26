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
};

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
  return `${apiBaseUrl}${normalizedPath}`;
};

export const apiRequest = async <T>(
  path: string,
  { method = "GET", body, auth = true }: RequestOptions = {}
): Promise<T> => {
  const response = await fetch(buildApiUrl(path), {
    method,
    headers: buildHeaders(auth),
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  const payload = (await response.json()) as ApiResponse<T>;

  if (!response.ok || payload.error) {
    throw new Error(payload.error?.error ?? "Request failed.");
  }

  return payload.data;
};
