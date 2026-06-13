import { useEffect, useState } from "react";

import { apiRequest } from "../api";
import { navItems as defaultNavItems, providerMeta } from "../constants";
import type {
  ProviderStatus,
  ProviderUpdateResponse,
  ProvidersResponse,
  NavItem,
  UserSettingsResponse,
} from "../types";
import { formatProviderName } from "../utils/formatters";
import { AppFooter } from "../components/AppFooter";
import { AppHeader } from "../components/AppHeader";
import { Background } from "../components/Background";

type SettingsPageProps = {
  path: string;
  navItems?: NavItem[];
  onNavigate: (path: string) => void;
  onLogout: () => void;
  theme: "light" | "dark";
  onToggleTheme: () => void;
};

export const SettingsPage = ({
  path,
  navItems = defaultNavItems,
  onNavigate,
  onLogout,
  theme,
  onToggleTheme,
}: SettingsPageProps) => {
  const [providerStatus, setProviderStatus] = useState<ProviderStatus[]>([]);
  const [isProvidersLoading, setIsProvidersLoading] = useState(false);
  const [providersError, setProvidersError] = useState("");
  const [ignoreShortSessionsMinutes, setIgnoreShortSessionsMinutes] =
    useState("0");
  const [isSettingsLoading, setIsSettingsLoading] = useState(false);
  const [isSettingsSaving, setIsSettingsSaving] = useState(false);
  const [settingsError, setSettingsError] = useState("");
  const [updatingProviderIds, setUpdatingProviderIds] = useState<number[]>([]);
  const visibleProviders = providerStatus.filter(
    (provider) =>
      provider.platform !== "manual" && provider.platform !== "local"
  );

  useEffect(() => {
    let cancelled = false;
    setIsProvidersLoading(true);
    setProvidersError("");

    const loadProviders = async () => {
      try {
        const data = await apiRequest<ProvidersResponse>("/providers");
        if (cancelled) {
          return;
        }

        setProviderStatus(data.providers);
        setIsProvidersLoading(false);
      } catch (err) {
        if (!cancelled) {
          setProviderStatus([]);
          setIsProvidersLoading(false);
          setProvidersError(
            err instanceof Error ? err.message : "Failed to load providers."
          );
        }
      }
    };

    loadProviders();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    setIsSettingsLoading(true);
    setSettingsError("");

    const loadSettings = async () => {
      try {
        const data = await apiRequest<UserSettingsResponse>("/settings");
        if (cancelled) {
          return;
        }
        setIgnoreShortSessionsMinutes(
          String(data.ignoreShortSessionsMinutes ?? 0)
        );
        setIsSettingsLoading(false);
      } catch (err) {
        if (!cancelled) {
          setIsSettingsLoading(false);
          setSettingsError(
            err instanceof Error ? err.message : "Failed to load settings."
          );
        }
      }
    };

    loadSettings();
    return () => {
      cancelled = true;
    };
  }, []);

  const handleSettingsSave = async () => {
    const parsed = Number(ignoreShortSessionsMinutes);
    if (!Number.isInteger(parsed) || parsed < 0) {
      setSettingsError("Enter a non-negative number of minutes.");
      return;
    }

    setIsSettingsSaving(true);
    setSettingsError("");
    try {
      const response = await apiRequest<UserSettingsResponse>("/settings", {
        method: "PUT",
        body: { ignoreShortSessionsMinutes: parsed },
      });
      setIgnoreShortSessionsMinutes(
        String(response.ignoreShortSessionsMinutes ?? parsed)
      );
    } catch (err) {
      setSettingsError(
        err instanceof Error ? err.message : "Failed to save settings."
      );
    } finally {
      setIsSettingsSaving(false);
    }
  };

  const handleToggleProviderStatus = async (provider: ProviderStatus) => {
    const nextStatus = provider.status === "active" ? "paused" : "active";
    setUpdatingProviderIds((current) =>
      current.includes(provider.id) ? current : [...current, provider.id]
    );
    setProvidersError("");

    try {
      const response = await apiRequest<ProviderUpdateResponse>(
        "/providers/status",
        {
          method: "PUT",
          body: { providerId: provider.id, status: nextStatus },
        }
      );
      setProviderStatus((current) =>
        current.map((item) =>
          item.id === response.provider.id ? response.provider : item
        )
      );
    } catch (err) {
      setProvidersError(
        err instanceof Error ? err.message : "Failed to update provider status."
      );
    } finally {
      setUpdatingProviderIds((current) =>
        current.filter((id) => id !== provider.id)
      );
    }
  };

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-200 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl p-3 sm:px-6 sm:py-12">
        <AppHeader
          title="Settings"
          navItems={navItems}
          path={path}
          onNavigate={onNavigate}
          onLogout={onLogout}
          theme={theme}
          onToggleTheme={onToggleTheme}
        />

        <section className="mt-10 grid gap-6 lg:grid-cols-[1.05fr_0.95fr]">
          <div className="space-y-6">
            <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                Session filters
              </p>
              <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                Ignore short sessions
              </h2>
              <p className="mt-2 text-xs text-slate-500 dark:text-slate-400">
                Sessions shorter than this duration are marked ignored and
                excluded from stats. Set to 0 to disable.
              </p>
              <div className="mt-5 flex flex-wrap items-end gap-4">
                <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                  <span className="block">Minimum minutes</span>
                  <input
                    type="number"
                    min="0"
                    step="1"
                    inputMode="numeric"
                    value={ignoreShortSessionsMinutes}
                    onChange={(event) =>
                      setIgnoreShortSessionsMinutes(event.target.value)
                    }
                    disabled={isSettingsLoading || isSettingsSaving}
                    className="mt-3 w-32 rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:cursor-not-allowed disabled:opacity-60"
                  />
                </label>
                <button
                  type="button"
                  onClick={handleSettingsSave}
                  disabled={isSettingsLoading || isSettingsSaving}
                  className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
                >
                  {isSettingsSaving
                    ? "Saving..."
                    : isSettingsLoading
                      ? "Loading..."
                      : "Save"}
                </button>
              </div>
              {settingsError ? (
                <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">
                  {settingsError}
                </p>
              ) : null}
            </div>
          </div>

          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
              Manage providers
            </p>
            <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
              Active connections
            </h2>
            {providersError ? (
              <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">
                {providersError}
              </p>
            ) : null}
            <div className="mt-6 space-y-4">
              {isProvidersLoading ? (
                [1, 2].map((item) => (
                  <div
                    key={item}
                    className="flex items-center justify-between gap-4 rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4"
                  >
                    <div className="flex items-center gap-3">
                      <div className="h-10 w-10 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                      <div className="space-y-2">
                        <div className="h-3 w-24 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                        <div className="h-2 w-16 rounded-full bg-slate-200/50 dark:bg-slate-800/50" />
                      </div>
                    </div>
                    <div className="h-6 w-20 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                  </div>
                ))
              ) : visibleProviders.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 p-4 text-xs text-slate-500 dark:text-slate-400">
                  No providers connected yet. Add one to start syncing playtime.
                </div>
              ) : (
                visibleProviders.map((provider) => {
                  const isUpdating = updatingProviderIds.includes(provider.id);
                  return (
                    <div
                      key={provider.id}
                      className="flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4"
                    >
                      <div className="flex items-center gap-3">
                        <div
                          className={`flex h-10 w-10 items-center justify-center rounded-full ${
                            providerMeta[provider.platform]?.color ??
                            "bg-slate-200 dark:bg-slate-800"
                          }`}
                        >
                          <span
                            className={`text-xs font-semibold uppercase ${
                              providerMeta[provider.platform]?.accent ??
                              "text-slate-700 dark:text-slate-200"
                            }`}
                          >
                            {providerMeta[provider.platform]?.label ?? "PX"}
                          </span>
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <p className="text-sm font-semibold text-slate-900 dark:text-white">
                              {formatProviderName(provider.platform)}
                            </p>
                          </div>
                          <p className="text-xs text-slate-500 dark:text-slate-400">
                            {provider.platform === "playnite"
                              ? (provider.apiKey ?? "Local")
                              : (provider.accountId ?? "Local")}
                          </p>
                        </div>
                      </div>
                      <div className="flex flex-wrap items-center gap-2 text-xs">
                        <button
                          type="button"
                          onClick={() => {
                            void handleToggleProviderStatus(provider);
                          }}
                          disabled={isUpdating}
                          className={`rounded-full px-3 py-2 font-semibold uppercase tracking-[0.3em] disabled:cursor-not-allowed disabled:opacity-60 ${
                            provider.status === "active"
                              ? "bg-emerald-400 text-slate-900"
                              : "border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200"
                          }`}
                        >
                          {isUpdating
                            ? "Updating..."
                            : provider.status === "active"
                              ? "Active"
                              : "Paused"}
                        </button>
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        </section>
        <AppFooter />
      </div>
    </main>
  );
};
