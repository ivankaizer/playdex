import type { PlatformAccount, PlatformClient, PlatformGame } from "./platform";

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
  ): Promise<PlatformGame | null> {
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
      throw new Error(`Steam API error: ${response.status}`);
    }

    const payload = (await response.json()) as SteamPlayerSummaryResponse;
    const player = payload.response?.players?.[0];

    if (!player?.gameid || !player?.gameextrainfo) {
      return null;
    }

    return {
      platform: "steam",
      title: player.gameextrainfo,
      externalId: player.gameid,
      raw: player,
    };
  }
}
