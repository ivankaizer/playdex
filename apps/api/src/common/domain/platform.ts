export type PlatformAccount = {
  platform: string;
  accountId: string;
  displayName?: string;
};

export type PlatformGame = {
  platform: string;
  title: string;
  externalId?: string;
  capsuleUrl?: string;
  raw?: unknown;
};

export type PlatformPollResult = {
  platform: string;
  accountId: string;
  game: PlatformGame | null;
  raw: unknown;
};

export interface PlatformClient {
  platform: string;
  getCurrentlyPlaying(account: PlatformAccount): Promise<PlatformPollResult>;
}

