import {
  createPlayniteCredential,
  listProvidersForUser,
  loadProviderByPlatform,
  updateProviderActive,
  updateProviderApiKey,
} from "../../shared/db/queries";

export const providersRepository = {
  listProviders: (userId: number) => listProvidersForUser(userId),
  loadProviderByPlatform: (userId: number, platform: string) =>
    loadProviderByPlatform(userId, platform),
  updateProviderActive: (userId: number, providerId: number, active: boolean) =>
    updateProviderActive(userId, providerId, active),
  updateProviderApiKey: (providerId: number, apiKey: string) =>
    updateProviderApiKey(providerId, apiKey),
  createPlayniteCredential: (userId: number, apiKey: string) =>
    createPlayniteCredential(userId, apiKey),
};
