import type {
  PlatformAccount,
  PlatformClient,
  PlatformGame,
  PlatformPollResult,
} from "../../../shared/domain/platform";

type PsnAuthTokens = {
  accessToken: string;
  expiresAt: number;
  refreshToken?: string;
};

type PsnApiModule = {
  exchangeNpssoForCode: (npsso: string) => Promise<string>;
  exchangeCodeForAccessToken: (code: string) => Promise<{
    accessToken: string;
    expiresIn: number;
    refreshToken?: string;
  }>;
  getBasicPresence: (
    authorization: { accessToken: string },
    accountId: string
  ) => Promise<unknown>;
};

const ONE_MINUTE_MS = 60_000;

export class PsnClient implements PlatformClient {
  platform: PlatformClient["platform"] = "psn";
  private npsso: string;
  private tokens?: PsnAuthTokens;
  private psnApi?: PsnApiModule;

  constructor(options: { npsso: string }) {
    this.npsso = options.npsso;
  }

  private async getApi(): Promise<PsnApiModule> {
    if (this.psnApi) {
      return this.psnApi;
    }

    const psnApi = (await import("psn-api")) as unknown as PsnApiModule;
    this.psnApi = psnApi;
    return psnApi;
  }

  private async getAccessToken(): Promise<string> {
    if (this.tokens && this.tokens.expiresAt > Date.now() + ONE_MINUTE_MS) {
      return this.tokens.accessToken;
    }

    const api = await this.getApi();
    const code = await api.exchangeNpssoForCode(this.npsso);
    const tokenResponse = await api.exchangeCodeForAccessToken(code);

    this.tokens = {
      accessToken: tokenResponse.accessToken,
      refreshToken: tokenResponse.refreshToken,
      expiresAt: Date.now() + tokenResponse.expiresIn * 1000,
    };

    return this.tokens.accessToken;
  }

  private getAccountIdFromAccessToken(accessToken: string): string | null {
    const parts = accessToken.split(".");
    if (parts.length < 2) {
      return null;
    }

    const normalized = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, "=");

    let payload: string;
    try {
      payload = Buffer.from(padded, "base64").toString("utf8");
    } catch {
      return null;
    }

    try {
      const claims = JSON.parse(payload) as { account_id?: string };
      return typeof claims.account_id === "string" ? claims.account_id : null;
    } catch {
      return null;
    }
  }

  async getCurrentlyPlaying(
    account: PlatformAccount
  ): Promise<PlatformPollResult> {
    const api = await this.getApi();
    const accessToken = await this.getAccessToken();
    const presence = (await api.getBasicPresence(
      { accessToken },
      account.accountId
    )) as {
      basicPresence?: {
        gameTitleInfoList?: Array<{
          titleName?: string;
          npTitleId?: string;
          titleId?: string;
          conceptIconUrl?: string;
        }>;
      };
    };

    const titleInfo = presence?.basicPresence?.gameTitleInfoList?.[0];
    let game: PlatformGame | null = null;
    if (titleInfo?.titleName) {
      game = {
        platform: "psn",
        title: titleInfo.titleName,
        externalId: titleInfo.npTitleId ?? titleInfo.titleId,
        capsuleUrl: titleInfo.conceptIconUrl,
        raw: presence,
      };
    }

    return {
      platform: "psn",
      accountId: account.accountId,
      raw: presence,
      game,
    };
  }

  async resolveAccountId(_onlineId?: string): Promise<string | null> {
    const accessToken = await this.getAccessToken();
    return this.getAccountIdFromAccessToken(accessToken);
  }
}
