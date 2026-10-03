export interface SentryConfig {
  dsn: string | null;
  environment: string;
  release: string | null;
  tracesSampleRate: number;
}

export interface AppConfig {
  apiBase: string;
  sentry: SentryConfig | null;
}

interface RawSettings {
  ApiBaseUrl?: string;
  Sentry?: {
    Dsn?: string;
    Environment?: string;
    Release?: string;
    TracesSampleRate?: number | string;
  };
}

let current: AppConfig = { apiBase: defaultApiBase(), sentry: null };

export function getConfig(): AppConfig {
  return current;
}

export function setConfig(config: AppConfig): void {
  current = config;
}

/** Normalizes values left as `__PLACEHOLDER__` by the container entrypoint. */
export function normalizeSetting(value: string | null | undefined): string | null {
  const normalized = value?.trim();
  if (!normalized || (normalized.startsWith("__") && normalized.endsWith("__"))) return null;
  return normalized;
}

function defaultApiBase(): string {
  return new URL("/api/", window.location.origin).href;
}

export async function loadConfig(): Promise<AppConfig> {
  let raw: RawSettings = {};
  try {
    const response = await fetch("/appsettings.json", { cache: "no-store" });
    if (response.ok) raw = (await response.json()) as RawSettings;
  } catch {
    raw = {};
  }

  const apiBase =
    normalizeSetting(raw.ApiBaseUrl) ??
    normalizeSetting(import.meta.env.VITE_API_BASE_URL as string | undefined) ??
    defaultApiBase();

  const config: AppConfig = { apiBase, sentry: await resolveSentry(raw, apiBase) };
  setConfig(config);
  return config;
}

async function resolveSentry(raw: RawSettings, apiBase: string): Promise<SentryConfig | null> {
  const fallbackEnvironment = import.meta.env.MODE === "production" ? "Production" : "Development";
  const localDsn = normalizeSetting(raw.Sentry?.Dsn);
  if (localDsn) {
    return {
      dsn: localDsn,
      environment: normalizeSetting(raw.Sentry?.Environment) ?? fallbackEnvironment,
      release: normalizeSetting(raw.Sentry?.Release),
      tracesSampleRate: parseSampleRate(raw.Sentry?.TracesSampleRate),
    };
  }

  try {
    const url = new URL("observability/sentry", apiBase.replace(/\/+$/, "") + "/");
    const response = await fetch(url, { signal: AbortSignal.timeout(3000) });
    if (!response.ok) return null;
    const envelope = (await response.json()) as { data?: Partial<SentryConfig> | null };
    const remote = envelope.data;
    const dsn = normalizeSetting(remote?.dsn ?? null);
    if (!remote || !dsn) return null;
    return {
      dsn,
      environment: remote.environment ?? fallbackEnvironment,
      release: remote.release ?? null,
      tracesSampleRate: parseSampleRate(remote.tracesSampleRate),
    };
  } catch (error) {
    console.error(`Sentry bootstrap unavailable: ${(error as Error).message}`);
    return null;
  }
}

function parseSampleRate(value: number | string | undefined): number {
  const parsed = typeof value === "number" ? value : Number.parseFloat(value ?? "");
  return Number.isFinite(parsed) && parsed >= 0 && parsed <= 1 ? parsed : 0.2;
}
