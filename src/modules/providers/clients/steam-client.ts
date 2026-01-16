import type {
  PlatformAccount,
  PlatformClient,
  PlatformGame,
  PlatformPollResult,
} from "../../../shared/domain/platform";

type SteamPlayerSummaryResponse = {
  response?: {
    players?: Array<{
      steamid?: string;
      gameid?: string;
      gameextrainfo?: string;
    }>;
  };
};

export class SteamClient implements PlatformClient {
  platform: PlatformClient["platform"] = "steam";
  private apiKey: string;
  private defaultSteamId?: string;

  constructor(options: { apiKey: string; steamId?: string }) {
    this.apiKey = options.apiKey;
    this.defaultSteamId = options.steamId;
  }

  async getCurrentlyPlaying(
    account: PlatformAccount
  ): Promise<PlatformPollResult> {
    const steamId = account.accountId || this.defaultSteamId;
    if (!steamId) {
      throw new Error("Steam accountId is required to fetch current game.");
    }

    const url = new URL(
      "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/"
    );
    url.searchParams.set("key", this.apiKey);
    url.searchParams.set("steamids", steamId);

    const response = await fetch(url);
    if (!response.ok) {
      const error = new Error(`Steam API error: ${response.status}`);
      (error as Error & { status?: number }).status = response.status;
      if (response.status === 429) {
        const retryAfter = response.headers.get("retry-after");
        if (retryAfter) {
          const retryAfterSeconds = Number.parseInt(retryAfter, 10);
          const retryAfterMs = Number.isNaN(retryAfterSeconds)
            ? null
            : retryAfterSeconds * 1000;
          if (retryAfterMs) {
            (error as Error & { retryAfterMs?: number }).retryAfterMs =
              retryAfterMs;
          }
        }
      }
      throw error;
    }

    const payload = (await response.json()) as SteamPlayerSummaryResponse;
    const player = payload.response?.players?.[0];

    let game: PlatformGame | null = null;
    if (player?.gameid && player?.gameextrainfo) {
      game = {
        platform: "steam",
        title: player.gameextrainfo,
        externalId: player.gameid,
        raw: player,
      };
    }

    return {
      platform: "steam",
      accountId: steamId,
      raw: payload,
      game,
    };
  }
}
