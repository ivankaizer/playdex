import { Button, findClassModule, TextField } from '@steambrew/client';
import { useCallback, useEffect, useState } from 'react';
import { forceFakeLocationChange } from '../helpers.js';
import rpc from '../rpc.js';
import type Steam from '../steam.js';

const SettingsStyles = findClassModule((m) => m.SectionTopLine)!;

export function PlaydexGameSettings({ app }: { app: Steam.AppOverview }) {
  const [gameId, setGameId] = useState('');
  const [apiKey, setApiKey] = useState('');

  useEffect(() => {
    const load = async () => {
      const [gid, key] = await Promise.all([
        rpc.GetGameId(app),
        rpc.GetGameApiKey(app),
      ]);

      setGameId(gid ?? '');
      setApiKey(key ?? '');
    };

    load();
  }, [app]);

  const [saveState, setSaveState] = useState('Save');

  const updateSettings = useCallback(async () => {
    try {
      setSaveState('Saving...');

      const results = await Promise.all([
        rpc.SetGameId(app, gameId),
        rpc.OnGameApiKeyFieldChange(app, apiKey),
      ]);

      if (!results.every(Boolean)) throw new Error('Save failed');

      setSaveState('Saved');
    } catch (_e) {
      setSaveState('Failed');
    } finally {
      setTimeout(() => {
        setSaveState('Save');
        // Force location monitor to detect a "refresh" of the page to
        // instantly update the playtime
        forceFakeLocationChange();
      }, 2000);
    }
  }, [app, gameId, apiKey]);

  return (
    <div>
      <div>Set custom settings for the game</div>

      <div style={{ marginTop: 8 }}>Set custom game ID for the game</div>
      <div className={SettingsStyles.AsyncBackedInputChildren}>
        <TextField
          // @ts-expect-error - placeholder is a valid prop but not typed
          placeholder="e.g. 730 - for CS2"
          value={gameId}
          onChange={(e) => {
            setGameId(e.target.value || '');
          }}
        />
      </div>

      <div style={{ marginTop: 12 }}>
        Override API key for this game (optional)
      </div>
      <div className={SettingsStyles.AsyncBackedInputChildren}>
        <TextField
          // @ts-expect-error - placeholder is a valid prop but not typed
          placeholder="leave empty to use global API key"
          value={apiKey}
          onChange={(e) => {
            setApiKey(e.target.value || '');
          }}
        />
        <Button
          className={`${SettingsStyles.SettingsDialogButton} ${SettingsStyles.ShortcutChange} DialogButton`}
          onClick={updateSettings}
        >
          {saveState}
        </Button>
      </div>
    </div>
  );
}
