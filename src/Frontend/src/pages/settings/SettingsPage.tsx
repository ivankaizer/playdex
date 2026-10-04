import { useEffect } from "react";
import { useLocation } from "react-router";
import { AppLayout } from "@/components/AppLayout";
import { IntegrationsSection } from "./integrations/IntegrationsSection";
import { useIntegrations } from "./integrations/useIntegrations";
import { MappingSection } from "./mapping/MappingSection";
import { PreferencesSection } from "./preferences/PreferencesSection";
import { usePreferences } from "./preferences/usePreferences";

export function SettingsPage() {
  const integrations = useIntegrations();
  const preferences = usePreferences();
  const { loadProviders } = integrations;
  const { loadSettings } = preferences;

  // Initial load: providers then settings, launched together without waiting for either.
  useEffect(() => {
    void loadProviders();
    void loadSettings();
  }, [loadProviders, loadSettings]);

  useScrollToHash();

  return (
    <AppLayout title="Settings">
      <PreferencesSection preferences={preferences} />
      <IntegrationsSection integrations={integrations} />
      <MappingSection />
    </AppLayout>
  );
}

/**
 * Blazor's Router scrolls the element named by the URL fragment into view after navigating
 * (e.g. the legacy /mapping redirect to /settings#game-connections).
 */
function useScrollToHash() {
  const { hash, key } = useLocation();
  useEffect(() => {
    const identifier = hash.startsWith("#") ? hash.slice(1) : hash;
    if (!identifier) return;
    const element = document.getElementById(identifier) ?? document.getElementsByName(identifier)[0];
    element?.scrollIntoView();
  }, [hash, key]);
}
