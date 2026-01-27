import { useEffect, useState } from "react";

import { apiRequest } from "../api";
import { navItems as defaultNavItems, providerMeta } from "../constants";
import type {
  PlayniteKeyResponse,
  ProviderStatus,
  ProvidersResponse,
  NavItem,
  UserSettingsResponse,
} from "../types";
import { formatProviderName } from "../utils/formatters";
import { AppHeader } from "../components/AppHeader";
import { Background } from "../components/Background";

type SettingsPageProps = {
  path: string;
  navItems?: NavItem[];
  onNavigate: (path: string) => void;
  onLogout: () => void;
};

export const SettingsPage = ({
  path,
  navItems = defaultNavItems,
  onNavigate,
  onLogout,
}: SettingsPageProps) => {
  const [playniteKey, setPlayniteKey] = useState("");
  const [isPlayniteKeyLoading, setIsPlayniteKeyLoading] = useState(false);
  const [providerStatus, setProviderStatus] = useState<ProviderStatus[]>([]);
  const [isProvidersLoading, setIsProvidersLoading] = useState(false);
  const [providersError, setProvidersError] = useState("");
  const [ignoreShortSessionsMinutes, setIgnoreShortSessionsMinutes] =
    useState("0");
  const [isSettingsLoading, setIsSettingsLoading] = useState(false);
  const [isSettingsSaving, setIsSettingsSaving] = useState(false);
  const [settingsError, setSettingsError] = useState("");

  const generatePlayniteKey = async () => {
    setIsPlayniteKeyLoading(true);
    setProvidersError("");
    try {
      const response = await apiRequest<PlayniteKeyResponse>("/playnite/key", {
        method: "POST",
      });
      setPlayniteKey(response.apiKey);
    } catch (err) {
      setProvidersError(
        err instanceof Error ? err.message : "Failed to generate key."
      );
    } finally {
      setIsPlayniteKeyLoading(false);
    }
  };

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
        const playnite = data.providers.find(
          (provider) => provider.platform === "playnite"
        );
        if (playnite?.apiKey) {
          setPlayniteKey(playnite.apiKey);
        }
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

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl p-3 sm:px-6 sm:py-12">
        <AppHeader
          title="Settings"
          navItems={navItems}
          path={path}
          onNavigate={onNavigate}
          onLogout={onLogout}
        />

        <section className="mt-10 grid gap-6 lg:grid-cols-[1.05fr_0.95fr]">
          <div className="space-y-6">
            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-5 sm:p-6">
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                Session filters
              </p>
              <h2 className="mt-2 text-xl font-semibold text-white">
                Ignore short sessions
              </h2>
              <p className="mt-2 text-xs text-slate-400">
                Sessions shorter than this duration are marked ignored and
                excluded from stats. Set to 0 to disable.
              </p>
              <div className="mt-5 flex flex-wrap items-end gap-4">
                <label className="text-xs font-medium text-slate-300">
                  Minimum minutes
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
                    className="mt-2 w-32 rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:cursor-not-allowed disabled:opacity-60"
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
                <p className="mt-3 text-xs text-rose-300">{settingsError}</p>
              ) : null}
            </div>
            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-5 sm:p-6">
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                Add provider
              </p>
              <h2 className="mt-2 text-xl font-semibold text-white">
                Connect new accounts
              </h2>
              <div className="mt-6 grid gap-4">
                <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                  <p className="text-sm font-semibold text-white">Steam</p>
                  <div className="mt-4 grid gap-3 sm:grid-cols-2">
                    <label className="text-xs font-medium text-slate-300">
                      API Key
                      <input
                        type="text"
                        placeholder="STEAM_API_KEY"
                        className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                      />
                    </label>
                    <label className="text-xs font-medium text-slate-300">
                      Account ID
                      <input
                        type="text"
                        placeholder="7656119..."
                        className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                      />
                    </label>
                  </div>
                  <button
                    type="button"
                    className="mt-4 rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300"
                  >
                    Add Steam
                  </button>
                </div>

                <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                  <p className="text-sm font-semibold text-white">PSN</p>
                  <label className="mt-4 block text-xs font-medium text-slate-300">
                    NPSSO Code
                    <input
                      type="text"
                      placeholder="NPSSO..."
                      className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                    />
                  </label>
                  <button
                    type="button"
                    className="mt-4 rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300"
                  >
                    Add PSN
                  </button>
                </div>

                <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                  <p className="text-sm font-semibold text-white">Playnite</p>
                  <p className="mt-2 text-xs text-slate-400">
                    Generate an API key for the local Playnite client.
                  </p>
                  <div className="mt-4 flex flex-wrap items-center gap-3">
                    <button
                      type="button"
                      onClick={generatePlayniteKey}
                      disabled={isPlayniteKeyLoading}
                      className="rounded-full border border-emerald-400/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-emerald-200 transition hover:border-emerald-300"
                    >
                      {isPlayniteKeyLoading ? "Generating..." : "Generate key"}
                    </button>
                    <span className="rounded-full border border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200">
                      {playniteKey || "Not generated yet"}
                    </span>
                  </div>
                  <button
                    type="button"
                    className="mt-4 rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300"
                  >
                    Save Playnite
                  </button>
                </div>
              </div>
            </div>
          </div>

          <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-5 sm:p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
              Manage providers
            </p>
            <h2 className="mt-2 text-xl font-semibold text-white">
              Active connections
            </h2>
            {providersError ? (
              <p className="mt-4 text-xs text-rose-300">{providersError}</p>
            ) : null}
            <div className="mt-6 space-y-4">
              {isProvidersLoading ? (
                [1, 2].map((item) => (
                  <div
                    key={item}
                    className="flex items-center justify-between gap-4 rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                  >
                    <div className="flex items-center gap-3">
                      <div className="h-10 w-10 rounded-full bg-slate-800/60" />
                      <div className="space-y-2">
                        <div className="h-3 w-24 rounded-full bg-slate-800/60" />
                        <div className="h-2 w-16 rounded-full bg-slate-800/50" />
                      </div>
                    </div>
                    <div className="h-6 w-20 rounded-full bg-slate-800/60" />
                  </div>
                ))
              ) : providerStatus.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/40 p-4 text-xs text-slate-400">
                  No providers connected yet. Add one to start syncing playtime.
                </div>
              ) : (
                providerStatus.map((provider) => (
                  <div
                    key={provider.id}
                    className="flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                  >
                    <div className="flex items-center gap-3">
                      <div
                        className={`flex h-10 w-10 items-center justify-center rounded-full ${
                          providerMeta[provider.platform]?.color ??
                          "bg-slate-800"
                        }`}
                      >
                        <span
                          className={`text-xs font-semibold uppercase ${
                            providerMeta[provider.platform]?.accent ??
                            "text-slate-200"
                          }`}
                        >
                          {providerMeta[provider.platform]?.label ?? "PX"}
                        </span>
                      </div>
                      <div>
                        <div className="flex items-center gap-2">
                          <p className="text-sm font-semibold text-white">
                            {formatProviderName(provider.platform)}
                          </p>
                        </div>
                        <p className="text-xs text-slate-400">
                          {provider.accountId ?? "Local"}
                        </p>
                      </div>
                    </div>
                    <div className="flex flex-wrap items-center gap-2 text-xs">
                      <button
                        type="button"
                        onClick={() =>
                          setProviderStatus((current) =>
                            current.map((item) =>
                              item.id === provider.id
                                ? {
                                    ...item,
                                    status:
                                      item.status === "active"
                                        ? "paused"
                                        : "active",
                                  }
                                : item
                            )
                          )
                        }
                        className={`rounded-full px-3 py-2 font-semibold uppercase tracking-[0.3em] ${
                          provider.status === "active"
                            ? "bg-emerald-400 text-slate-900"
                            : "border border-slate-700 text-slate-200"
                        }`}
                      >
                        {provider.status === "active" ? "Active" : "Paused"}
                      </button>
                      <button
                        type="button"
                        className="rounded-full border border-slate-700 px-3 py-2 font-semibold uppercase tracking-[0.3em] text-slate-200"
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="rounded-full border border-rose-500/50 px-3 py-2 font-semibold uppercase tracking-[0.3em] text-rose-300"
                      >
                        Remove
                      </button>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        </section>
      </div>
    </main>
  );
};
