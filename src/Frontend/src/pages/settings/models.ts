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
  apiKey: string | null;
  apiKeyLocked: boolean;
  sessionPlatform?: ProviderPlatform | null;
  sessionPlatformOptions?: ProviderPlatform[] | null;
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
