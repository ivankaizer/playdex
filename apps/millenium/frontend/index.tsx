import { definePlugin, IconsModule, TextField } from '@steambrew/client';
import { useEffect, useState } from 'react';
import { register as registerAppProperties } from './app-properties.js';
import { NON_STEAM_APP_APPID_MASK } from './constants.js';
import { forceFakeLocationChange } from './helpers.js';
import rpc from './rpc.js';
import { onAppLaunch } from './running-apps.js';

export { RPC } from './rpc.js';

const SettingsContent = () => {
  const [apiKey, setApiKey] = useState<string>('');
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    const load = async () => {
      const key = await rpc.GetApiKey();
      setApiKey(key ?? '');
      setLoaded(true);
    };

    load();
  }, []);

  useEffect(() => {
    if (!loaded) return;

    const t = setTimeout(() => {
      rpc.OnApiKeyFieldChange(apiKey);
    }, 300);

    return () => clearTimeout(t);
  }, [apiKey, loaded]);

  return (
    <TextField
      onChange={(e) => {
        setApiKey(e.target.value);
      }}
      label="API key"
      value={apiKey}
    />
  );
};

export default definePlugin(() => {
  registerAppProperties();
  OnPluginLoaded();
  return {
    title: 'Playdex',
    icon: <IconsModule.Settings />,
    content: <SettingsContent />,
  };
});

async function OnPluginLoaded() {
  onAppLaunch((app, { onPing, onQuit }) => {
    if (app.appid < NON_STEAM_APP_APPID_MASK) return;

    onPing(() => {
      rpc.OnNonSteamAppHeartbeat(app);
      forceFakeLocationChange();
    });

    onQuit(() => {
      rpc.OnNonSteamAppQuit(app);
    });
  });
}
