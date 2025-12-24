export type Platform = "psn" | "steam";

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

export interface PlatformClient {
  platform: Platform;
  getCurrentlyPlaying(account: PlatformAccount): Promise<PlatformGame | null>;
}
