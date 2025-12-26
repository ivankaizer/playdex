import {
  createPlayniteCredential,
  listProvidersForUser,
  loadProviderByPlatform,
  updateProviderApiKey,
} from "../infrastructure/db/queries";

const generateApiKey = () => {
  const bytes = new Uint8Array(16);
  if (globalThis.crypto?.getRandomValues) {
    globalThis.crypto.getRandomValues(bytes);
  } else {
    for (let index = 0; index < bytes.length; index += 1) {
      bytes[index] = Math.floor(Math.random() * 256);
    }
  }
  const token = Array.from(bytes)
    .map((byte) => byte.toString(16).padStart(2, "0"))
    .join("");
  return `plx_${token}`;
};

export const getProviders = async (userId: number) => {
  const rows = await listProvidersForUser(userId);
  return rows.map((row) => ({
    id: row.id,
    platform: row.platform,
    accountId: row.account_id,
    status: row.active ? "active" : "paused",
    apiKey: row.api_key,
  }));
};

export const getOrCreatePlayniteKey = async (userId: number) => {
  const existing = await loadProviderByPlatform(userId, "playnite");
  if (existing?.api_key) {
    return existing.api_key;
  }

  const apiKey = generateApiKey();
  if (existing && !existing.api_key) {
    const updated = await updateProviderApiKey(existing.id, apiKey);
    return updated?.api_key ?? apiKey;
  }

  const created = await createPlayniteCredential(userId, apiKey);
  return created[0]?.api_key ?? apiKey;
};
