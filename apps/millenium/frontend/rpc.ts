import { Millennium } from '@steambrew/client';
import { jsonReplacer, jsonReviver } from './helpers.js';
import type Steam from './steam.js';

/**
 * Call a method on the server
 * @param route The route to call, must be prefixed with `RPC.`
 * @param payload The payload to send
 * @returns The response from the server
 */
function call<R>(route: `RPC.${string}`, payload: object): Promise<R> {
  // Millennium build step for callable uses find and replace which doesn't always work
  // (Sometimes the client variable will be called client$1 if you have another variable called client)
  return Millennium.callServerMethod(route, {
    payload: JSON.stringify(payload, jsonReplacer),
  }).then((r) => JSON.parse(r, jsonReviver));
}

export class RPC {
  async OnNonSteamAppHeartbeat(app: Steam.AppOverview) {
    await call('RPC.OnNonSteamAppHeartbeat', {
      app_name: app.display_name,
    });
  }

  async OnNonSteamAppQuit(app: Steam.AppOverview) {
    await call('RPC.OnNonSteamAppQuit', {
      app_name: app.display_name,
    });
  }

  // ----------------------------------------------------------------
  // API keys (global + per-game override)
  // ----------------------------------------------------------------

  async GetApiKey() {
    return (await call<string>('RPC.GetApiKey', {})) || '';
  }

  async OnApiKeyFieldChange(key: string) {
    return await call<boolean>('RPC.OnApiKeyFieldChange', {
      key,
    });
  }

  async GetGameApiKey(app: Steam.AppOverview) {
    return (
      (await call<string>('RPC.GetGameApiKey', {
        app_name: app.display_name,
      })) || ''
    );
  }

  async OnGameApiKeyFieldChange(app: Steam.AppOverview, key: string) {
    return await call<boolean>('RPC.OnGameApiKeyFieldChange', {
      app_name: app.display_name,
      key,
    });
  }

  // ----------------------------------------------------------------
  // Game IDs
  // ----------------------------------------------------------------

  async SetGameId(app: Steam.AppOverview, id: string) {
    return await call<boolean>('RPC.SetGameId', {
      app_name: app.display_name,
      id,
    });
  }

  async GetGameId(app: Steam.AppOverview) {
    return (
      (await call<string>('RPC.GetGameId', { app_name: app.display_name })) ||
      ''
    );
  }
}

export default new RPC();
