import { PsnClient } from "../providers/clients/psn-client";
import { SteamClient } from "../providers/clients/steam-client";
import { pollingRepository } from "./polling.repository";

import type { PollingAccountEntry } from "./polling";

export const loadPollingAccounts = async (): Promise<PollingAccountEntry[]> => {
  const accounts: PollingAccountEntry[] = [];
  const credentials = await pollingRepository.loadPlatformCredentials();

  for (const credential of credentials) {
    if (credential.platform === "playnite") {
      continue;
    }

    if (credential.platform === "steam") {
      if (!credential.api_key?.trim() || !credential.account_id?.trim()) {
        console.warn(
          `Missing Steam credentials or account ID; skipping Steam polling (credential ${credential.id}).`
        );
        continue;
      }

      const steamClient = new SteamClient({
        apiKey: credential.api_key,
        steamId: credential.account_id,
      });
      accounts.push({
        platformCredentialId: credential.id,
        account: {
          platform: "steam",
          accountId: credential.account_id,
        },
        client: steamClient,
      });
      continue;
    }

    if (credential.platform === "psn") {
          if (!credential.api_key?.trim()) {
            console.warn(
              `Missing PSN credentials; skipping PSN polling (credential ${credential.id}).`
            );
            continue;
      }

      const psnClient = new PsnClient({ npsso: credential.api_key });
      let psnAccountId = credential.account_id?.trim() ?? "";
        if (!psnAccountId) {
          const resolved = await psnClient.resolveAccountId();
          if (resolved) {
            psnAccountId = resolved;
            console.log(`Resolved PSN account ID: ${psnAccountId}`);
            await pollingRepository.storeResolvedAccountId(
              credential.id,
              psnAccountId
            );
          } else {
            console.warn(
              `Could not resolve PSN account ID; skipping PSN polling (credential ${credential.id}).`
            );
          }
      }

      if (psnAccountId) {
        accounts.push({
          platformCredentialId: credential.id,
          account: {
            platform: "psn",
            accountId: psnAccountId,
          },
          client: psnClient,
        });
      }
      continue;
    }

    console.warn(
      `Unsupported platform ${credential.platform}; skipping polling (credential ${credential.id}).`
    );
  }

  return accounts;
};
