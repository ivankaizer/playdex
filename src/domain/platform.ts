export type Platform =
  | "ps5"
  | "xbox-series-xs"
  | "pc"
  | "switch-1"
  | "switch-2"
  | "steam"
  | "psn"
  | "playnite"
  | "manual"
  | "unknown"
  ;

export type PlatformAccount = {
  platform: Platform;
  accountId: string;
  displayName?: string;
};

export type PlatformGame = {
  platform: Platform;
  title: string;
  externalId?: string;
  capsuleUrl?: string;
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
