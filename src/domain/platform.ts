export type Platform = "psn" | "steam" | "unknown";

export type PlatformAccount = {
  platform: Platform;
  accountId: string;
  displayName?: string;
};

export type PlatformGame = {
  platform: Platform;
  title: string;
  externalId?: string;
  raw?: unknown;
};

export type PlatformPollResult = {
  platform: Platform;
  accountId: string;
  game: PlatformGame | null;
  raw: unknown;
};

export interface PlatformClient {
  platform: Platform;
  getCurrentlyPlaying(account: PlatformAccount): Promise<PlatformPollResult>;
}
