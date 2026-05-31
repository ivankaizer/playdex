import { definePlugin, IconsModule, TextField } from '@steambrew/client';
import { useEffect, useState } from 'react';
import { forceFakeLocationChange } from './helpers.js';
import rpc from './rpc.js';
import { onAppLaunch } from './running-apps.js';
import { NON_STEAM_APP_APPID_MASK } from './constants.js';
import { register as registerAppProperties } from './app-properties.js';

export { RPC } from './rpc.js';

const SettingsContent = () => {
  const [apiKey, setApiKey] = useState<string>('');

  useEffect(() => {
    const load = async () => {
      const key = await rpc.GetApiKey();
      setApiKey(key ?? '');
    };

    load();
  }, []);

  useEffect(() => {
    if (!apiKey) return;

    const t = setTimeout(() => {
      rpc.OnApiKeyFieldChange(apiKey);
    }, 300);

    return () => clearTimeout(t);
  }, [apiKey]);

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
  onAppLaunch((app, { onLaunch, onHeartbeat, onQuit }) => {
    if (app.appid < NON_STEAM_APP_APPID_MASK) return;
    onLaunch(() => {
      rpc.OnNonSteamAppLaunch(app);
    });

    onHeartbeat(() => {
      rpc.OnNonSteamAppHeartbeat(app);
      forceFakeLocationChange();
    });

    onQuit(() => {
      rpc.OnNonSteamAppQuit(app);
    });
  });
}
