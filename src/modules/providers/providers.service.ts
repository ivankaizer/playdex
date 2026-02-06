import { randomBytes } from "node:crypto";

import { AppError } from "../../common/errors/AppError";
import { providersRepository } from "./providers.repository";

const generateApiKey = () => {
  const token = randomBytes(16).toString("hex");
  return `plx_${token}`;
};

export const providersService = {
  getProviders: async (userId: number) => {
    const rows = await providersRepository.listProviders(userId);
    return rows.map((row) => ({
      id: row.id,
      platform: row.platform,
      accountId: row.account_id,
      status: row.active ? "active" : "paused",
      apiKey: row.api_key,
    }));
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

    return {
      id: updated.id,
      platform: updated.platform,
      accountId: updated.account_id,
      status: updated.active ? "active" : "paused",
      apiKey: updated.api_key,
    };
  },
};
