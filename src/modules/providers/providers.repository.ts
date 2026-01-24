import {
  createPlayniteCredential,
  listProvidersForUser,
  loadProviderByPlatform,
  updateProviderApiKey,
} from "../../shared/db/queries";

export const providersRepository = {
  listProviders: (userId: number) => listProvidersForUser(userId),
  loadProviderByPlatform: (userId: number, platform: string) =>
    loadProviderByPlatform(userId, platform),
  updateProviderApiKey: (providerId: number, apiKey: string) =>
    updateProviderApiKey(providerId, apiKey),
  createPlayniteCredential: (userId: number, apiKey: string) =>
    createPlayniteCredential(userId, apiKey),
};

