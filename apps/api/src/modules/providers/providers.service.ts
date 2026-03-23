import { randomBytes } from "node:crypto";

import { AppError } from "../../common/errors/AppError";
import type { ProviderStatusDto } from "../../app/http/types";
import { providersRepository } from "./providers.repository";

const generateApiKey = () => {
  const token = randomBytes(16).toString("hex");
  return `plx_${token}`;
};

const toProviderResponse = (row: {
  id: number;
  platform: string;
  account_id: string | null;
  active: boolean;
  api_key: string | null;
}): ProviderStatusDto => ({
  id: row.id,
  platform: row.platform,
  accountId: row.account_id,
  status: row.active ? "active" : "paused",
  apiKey: row.platform === "playnite" ? row.api_key : null,
});

export const providersService = {
  getProviders: async (userId: number) => {
    const rows = await providersRepository.listProviders(userId);
    return rows.map(toProviderResponse);
  },

  getOrCreatePlayniteKey: async (userId: number) => {
    const existing = await providersRepository.loadProviderByPlatform(
      userId,
      "playnite"
    );
    if (existing?.api_key) {
      return existing.api_key;
    }

    const apiKey = generateApiKey();
    if (existing && !existing.api_key) {
      const updated = await providersRepository.updateProviderApiKey(
        existing.id,
        apiKey
      );
      return updated?.api_key ?? apiKey;
    }

    const created = await providersRepository.createPlayniteCredential(
      userId,
      apiKey
    );
    return created[0]?.api_key ?? apiKey;
  },

  updateProviderStatus: async (
    userId: number,
    providerId: number,
    status: "active" | "paused"
  ) => {
    const updated = await providersRepository.updateProviderActive(
      userId,
      providerId,
      status === "active"
    );
    if (!updated) {
      throw new AppError("Provider not found.", 404);
    }

    return toProviderResponse(updated);
  },
};
