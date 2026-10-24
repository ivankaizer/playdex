import type { HttpMethod } from "@/lib/api";

export interface ProviderPlatform {
  key: string;
  name: string;
}

export interface ProviderStatus {
  id: number;
  platform: string;
  accountId: string | null;
  status: string;
  /** Whether a key is saved; the server never sends it back. */
  hasApiKey: boolean;
  apiKeyLocked: boolean;
  sessionPlatform?: ProviderPlatform | null;
  sessionPlatformOptions?: ProviderPlatform[] | null;
  /** Why Playdex paused polling itself (the provider kept rejecting the credential); null otherwise. */
  pausedReason?: string | null;
  pausedAt?: string | null;
}

export interface ProvidersResponse {
  providers: ProviderStatus[];
}

export interface ProviderUpdateResponse {
  provider: ProviderStatus;
}

export interface ProviderCredentialUpdateResponse {
  provider: ProviderStatus;
}

export interface ProviderSessionPlatformUpdateResponse {
  provider: ProviderStatus;
}

export interface ProviderCreateResponse {
  provider: ProviderStatus;
}

/** A new Playdex app key, from POST /key. The only time the server shows it. */
export interface AppKeyResponse {
  apiKey: string;
}

export interface UserSettingsResponse {
  ignoreShortSessionsMinutes: number;
  telegramChatId: string | null;
}

export interface TestNotificationResponse {
  sent: boolean;
}

/** A request the page should send: path, method and JSON body (key order preserved). */
export interface Submission {
  path: string;
  method: HttpMethod;
  body: Record<string, unknown>;
}
