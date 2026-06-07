import { callable } from '@steambrew/client';
import type Steam from './steam.js';

type AppPayload = { app_name: string };
type KeyPayload = { key: string };
type GameKeyPayload = AppPayload & KeyPayload;
type GameIdPayload = AppPayload & { id: string };

const onNonSteamAppHeartbeat = callable<[AppPayload], boolean>(
  'OnNonSteamAppHeartbeat',
);
const onNonSteamAppQuit = callable<[AppPayload], boolean>('OnNonSteamAppQuit');
const onApiKeyFieldChange = callable<[KeyPayload], boolean>(
  'OnApiKeyFieldChange',
);
const onGameApiKeyFieldChange = callable<[GameKeyPayload], boolean>(
  'OnGameApiKeyFieldChange',
);
const getApiKey = callable<[], string>('GetApiKey');
const getGameApiKey = callable<[AppPayload], string>('GetGameApiKey');
const setGameId = callable<[GameIdPayload], boolean>('SetGameId');
const getGameId = callable<[AppPayload], string>('GetGameId');

function normalizeRpcString(value: unknown): string {
  if (typeof value !== 'string') return '';

  const trimmed = value.trim();
  if (trimmed.length >= 2 && trimmed.startsWith('"') && trimmed.endsWith('"')) {
    try {
      const parsed = JSON.parse(trimmed) as unknown;
      if (typeof parsed === 'string') return parsed;
    } catch {
      return value;
    }
  }

  return value;
}

export class RPC {
  async OnNonSteamAppHeartbeat(app: Steam.AppOverview) {
    await onNonSteamAppHeartbeat({
      app_name: app.display_name,
    });
  }

  async OnNonSteamAppQuit(app: Steam.AppOverview) {
    await onNonSteamAppQuit({
      app_name: app.display_name,
    });
  }

  // ----------------------------------------------------------------
  // API keys (global + per-game override)
  // ----------------------------------------------------------------

  async GetApiKey() {
    return normalizeRpcString(await getApiKey());
  }

  async OnApiKeyFieldChange(key: string) {
    return await onApiKeyFieldChange({
      key,
    });
  }

  async GetGameApiKey(app: Steam.AppOverview) {
    return normalizeRpcString(
      await getGameApiKey({
        app_name: app.display_name,
      }),
    );
  }

  async OnGameApiKeyFieldChange(app: Steam.AppOverview, key: string) {
    return await onGameApiKeyFieldChange({
      app_name: app.display_name,
      key,
    });
  }

  // ----------------------------------------------------------------
  // Game IDs
  // ----------------------------------------------------------------

  async SetGameId(app: Steam.AppOverview, id: string) {
    return await setGameId({
      app_name: app.display_name,
      id,
    });
  }

  async GetGameId(app: Steam.AppOverview) {
    return normalizeRpcString(await getGameId({ app_name: app.display_name }));
  }
}

export default new RPC();
