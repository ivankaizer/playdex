import type { ProviderStatus } from "../models";
import {
  integrationActionButtonClass,
  integrationStatusClass,
  isAnySessionPlatformSaving,
  isCredentialSaving,
  isEditingCredential,
  isProviderActionBlocked,
  isProviderMutationInProgress,
  isProviderUpdating,
  isSessionPlatformSaving,
  presentProvider,
  selectedSessionPlatformKey,
} from "./providersState";
import type { Integrations } from "./useIntegrations";

interface IntegrationCardProps {
  provider: ProviderStatus;
  integrations: Integrations;
}

export function IntegrationCard({ provider, integrations }: IntegrationCardProps) {
  const { state } = integrations;
  const presentation = presentProvider(provider);
  const isUpdating = isProviderUpdating(state, provider.id);
  const mutationInProgress = isProviderMutationInProgress(state, provider.id);
  const actionBlocked = isProviderActionBlocked(state, provider.id);
  const editing = isEditingCredential(state, provider.id);
  const credentialSaving = isCredentialSaving(state, provider.id);

  return (
    <article className="rounded-2xl border border-slate-200 bg-slate-100/60 p-4 dark:border-slate-800 dark:bg-slate-950/50 sm:p-5">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex min-w-0 items-center gap-3">
          <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full ${presentation.color}`}>
            <span className={`text-xs font-semibold uppercase ${presentation.accent}`}>{presentation.label}</span>
          </div>
          <div className="min-w-0">
            <p className="text-sm font-semibold text-slate-900 dark:text-white">{presentation.name}</p>
            <p className="truncate text-xs text-slate-500 dark:text-slate-400">{presentation.subtitle}</p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-xs">
          <span data-integration-status={provider.status} className={integrationStatusClass(provider)}>
            {isUpdating ? "Updating..." : provider.status === "active" ? "Active" : "Paused"}
          </span>
          <button
            type="button"
            onClick={() => integrations.toggleCredentialEditor(provider)}
            disabled={isAnySessionPlatformSaving(state) || mutationInProgress}
            className="rounded-full border border-slate-300 px-3 py-2 font-semibold uppercase tracking-[0.2em] text-slate-700 transition hover:border-emerald-400/60 hover:text-emerald-700 dark:border-slate-700 dark:text-slate-200 dark:hover:text-emerald-100"
          >
            {editing ? "Close" : "Manage"}
          </button>
        </div>
      </div>

      {editing ? (
        <div className="mt-5 border-t border-slate-200 pt-5 dark:border-slate-800">
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
              <span className="block">Account ID</span>
              <input
                type="text"
                value={state.editingAccountId}
                onChange={(event) => integrations.setEditingAccountId(event.target.value)}
                disabled={credentialSaving}
                className="mt-2 w-full rounded-xl border border-slate-300 bg-white/90 px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:bg-slate-950/80 dark:text-white"
              />
            </label>
            {provider.platform === "api" ? (
              <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                <span className="block">API key</span>
                <input
                  type="text"
                  value={state.editingApiKey}
                  readOnly
                  aria-readonly="true"
                  className="mt-2 w-full cursor-text rounded-xl border border-slate-300 bg-slate-200/40 px-3 py-2 font-mono text-sm text-slate-700 outline-none dark:border-slate-700 dark:bg-slate-950/60 dark:text-slate-300"
                />
                <span className="mt-1 block font-normal text-slate-500 dark:text-slate-400">
                  Generated locally and read-only.
                </span>
              </label>
            ) : provider.apiKeyLocked ? (
              <div className="text-xs font-medium text-slate-600 dark:text-slate-300">
                <span className="block">API key</span>
                <div className="mt-2 flex items-center gap-2 rounded-xl border border-slate-300 bg-slate-200/40 px-3 py-2 text-sm text-slate-500 dark:border-slate-700 dark:bg-slate-950/60 dark:text-slate-400">
                  <span className="inline-flex h-2 w-2 rounded-full bg-amber-400"></span>
                  Managed by administrator
                </div>
              </div>
            ) : (
              <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                <span className="block">API key</span>
                <input
                  type="text"
                  value={state.editingApiKey}
                  onChange={(event) => integrations.setEditingApiKey(event.target.value)}
                  disabled={credentialSaving}
                  className="mt-2 w-full rounded-xl border border-slate-300 bg-white/90 px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:bg-slate-950/80 dark:text-white"
                />
              </label>
            )}
          </div>

          {provider.sessionPlatform != null && provider.sessionPlatformOptions && provider.sessionPlatformOptions.length > 0 ? (
            <RecordingPlatform
              provider={provider}
              options={provider.sessionPlatformOptions}
              integrations={integrations}
              mutationInProgress={mutationInProgress}
            />
          ) : null}

          {state.credentialError ? (
            <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">{state.credentialError}</p>
          ) : null}

          <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
            <button
              type="button"
              onClick={() => void integrations.toggleProvider(provider)}
              disabled={actionBlocked}
              className={integrationActionButtonClass(provider)}
            >
              {isUpdating ? "Updating..." : provider.status === "active" ? "Pause" : "Resume"}
            </button>
            <button
              type="button"
              onClick={() => void integrations.saveCredential(provider)}
              disabled={actionBlocked}
              className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
            >
              {credentialSaving ? "Saving..." : "Save credentials"}
            </button>
          </div>
        </div>
      ) : null}
    </article>
  );
}

interface RecordingPlatformProps {
  provider: ProviderStatus;
  options: NonNullable<ProviderStatus["sessionPlatformOptions"]>;
  integrations: Integrations;
  mutationInProgress: boolean;
}

function RecordingPlatform({ provider, options, integrations, mutationInProgress }: RecordingPlatformProps) {
  const { state } = integrations;
  const saving = isSessionPlatformSaving(state, provider.id);

  return (
    <div className="mt-4 rounded-xl border border-slate-200 bg-white/60 p-3 dark:border-slate-800 dark:bg-slate-900/50">
      <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
        <span className="block">Recording platform</span>
        <select
          value={selectedSessionPlatformKey(state, provider)}
          onChange={(event) => integrations.changeSessionPlatform(provider, event.target.value)}
          disabled={mutationInProgress}
          className="mt-2 w-full rounded-xl border border-slate-300 bg-white/90 px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:bg-slate-950/80 dark:text-white"
        >
          {options.map((option) => (
            <option key={option.key} value={option.key}>
              {option.name}
            </option>
          ))}
        </select>
        <span className="mt-1 block font-normal text-slate-500 dark:text-slate-400">
          New sessions recorded by this integration use this platform.
        </span>
      </label>

      {state.pendingSessionPlatformProviderId === provider.id ? (
        <div
          role="alertdialog"
          aria-label="Confirm recording platform change"
          className="mt-3 border-t border-slate-200 pt-3 dark:border-slate-800"
        >
          <p className="text-xs text-slate-600 dark:text-slate-300">
            Should existing sessions from this integration also use the new platform?
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => void integrations.saveSessionPlatform(provider, true)}
              disabled={saving}
              className="rounded-full bg-emerald-400 px-3 py-2 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-900 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Future and existing sessions
            </button>
            <button
              type="button"
              onClick={() => void integrations.saveSessionPlatform(provider, false)}
              disabled={saving}
              className="rounded-full border border-slate-300 px-3 py-2 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-700 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:text-slate-200"
            >
              Future sessions only
            </button>
            <button
              type="button"
              onClick={integrations.cancelSessionPlatformChange}
              disabled={saving}
              className="px-3 py-2 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500 disabled:cursor-not-allowed disabled:opacity-60 dark:text-slate-400"
            >
              Cancel
            </button>
          </div>
        </div>
      ) : null}

      {state.sessionPlatformError ? (
        <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">{state.sessionPlatformError}</p>
      ) : null}
    </div>
  );
}
