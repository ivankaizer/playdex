import { cx } from "@/lib/cx";
import { platformColor } from "@/lib/platforms";
import type { ProviderStatus } from "../models";
import {
  isAnySessionPlatformSaving,
  isCredentialSaving,
  isEditingCredential,
  isProviderActionBlocked,
  isProviderMutationInProgress,
  isProviderUpdating,
  isSessionPlatformSaving,
  presentProvider,
  selectedSessionPlatformKey,
  truncateMiddle,
} from "./providersState";
import type { Integrations } from "./useIntegrations";

interface IntegrationCardProps {
  provider: ProviderStatus;
  integrations: Integrations;
}

const fieldLabelClass = "pdx-label flex min-w-0 flex-col gap-2";
const fieldInputClass = "pdx-input !h-11 tracking-normal";

export function IntegrationCard({ provider, integrations }: IntegrationCardProps) {
  const { state } = integrations;
  const presentation = presentProvider(provider);
  const isUpdating = isProviderUpdating(state, provider.id);
  const mutationInProgress = isProviderMutationInProgress(state, provider.id);
  const actionBlocked = isProviderActionBlocked(state, provider.id);
  const editing = isEditingCredential(state, provider.id);
  const credentialSaving = isCredentialSaving(state, provider.id);
  const active = provider.status === "active";
  const panelId = `integration-${provider.id}-manage`;

  return (
    <article className="rounded-tile border border-[var(--tile-border)] bg-tile">
      <div className="grid grid-cols-[48px_minmax(0,1fr)_auto_auto] items-center gap-x-4 gap-y-3 px-5 py-4 max-[600px]:flex max-[600px]:flex-wrap max-[600px]:gap-x-4 max-[600px]:gap-y-2.5 max-[600px]:px-4 max-[600px]:py-3.5">
        <div
          aria-hidden="true"
          className="grid h-12 w-12 shrink-0 place-items-center rounded-[14px] text-sm font-bold tracking-[0.04em] text-white"
          style={{ background: platformColor(provider.platform, presentation.name) }}
        >
          {presentation.label}
        </div>
        <div className="min-w-0 max-[600px]:flex-[1_1_calc(100%-64px)]">
          <p className="text-[15px] font-semibold">{presentation.name}</p>
          <p title={presentation.subtitle} className="pdx-num truncate text-[13px] text-muted">
            {truncateMiddle(presentation.subtitle)}
          </p>
        </div>
        <span
          data-integration-status={provider.status}
          className={cx(
            "inline-flex items-center gap-1.5 justify-self-start rounded-full px-3 py-1.5 text-xs font-semibold",
            active ? "bg-mint text-accent" : "bg-divider text-ink-3",
          )}
        >
          <span
            aria-hidden="true"
            className={cx("h-[7px] w-[7px] rounded-full", active ? "bg-brand" : "bg-faint")}
          ></span>
          {isUpdating ? "Updating..." : active ? "Active" : "Paused"}
        </span>
        <button
          type="button"
          aria-expanded={editing}
          aria-controls={editing ? panelId : undefined}
          onClick={() => integrations.toggleCredentialEditor(provider)}
          disabled={isAnySessionPlatformSaving(state) || mutationInProgress}
          className="pdx-btn-outline !h-9 !px-4 !text-[13px] max-[600px]:ml-auto"
        >
          {editing ? "Done" : "Manage"}
        </button>
      </div>

      {editing ? (
        <div id={panelId} className="flex flex-col gap-4 pb-5 pl-[84px] pr-5 max-[600px]:px-4 max-[600px]:pb-4">
          <div className="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(min(240px,100%),1fr))]">
            <label className={fieldLabelClass}>
              Account ID
              <input
                type="text"
                value={state.editingAccountId}
                onChange={(event) => integrations.setEditingAccountId(event.target.value)}
                disabled={credentialSaving}
                className={fieldInputClass}
              />
            </label>
            {provider.platform === "api" ? (
              <div className="flex min-w-0 flex-col gap-1.5">
                <label className={fieldLabelClass}>
                  API key
                  <input
                    type="text"
                    value={state.editingApiKey}
                    readOnly
                    aria-readonly="true"
                    className={cx(fieldInputClass, "!bg-tile font-mono text-ink-2")}
                  />
                </label>
                <span className="px-1 text-xs text-muted">Generated locally and read-only.</span>
              </div>
            ) : provider.apiKeyLocked ? (
              <div className={fieldLabelClass}>
                API key
                <div className="flex h-11 items-center gap-2 rounded-full border border-line bg-tile px-[18px] text-sm normal-case tracking-normal text-muted">
                  <span aria-hidden="true" className="h-2 w-2 rounded-full bg-warn"></span>
                  Managed by administrator
                </div>
              </div>
            ) : (
              <label className={fieldLabelClass}>
                API key
                <input
                  type="text"
                  value={state.editingApiKey}
                  onChange={(event) => integrations.setEditingApiKey(event.target.value)}
                  disabled={credentialSaving}
                  className={fieldInputClass}
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

          {state.credentialError ? <p className="text-[13px] text-danger">{state.credentialError}</p> : null}

          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => void integrations.saveCredential(provider)}
              disabled={actionBlocked}
              className="pdx-btn-accent !h-[34px] !px-3.5 !text-xs"
            >
              {credentialSaving ? "Saving..." : "Save credentials"}
            </button>
            <button
              type="button"
              onClick={() => void integrations.toggleProvider(provider)}
              disabled={actionBlocked}
              className="pdx-btn-outline !h-[34px] !px-3.5 !text-xs"
            >
              {isUpdating ? "Updating..." : active ? "Pause" : "Resume"}
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
    <div className="rounded-[20px] border border-line bg-surface p-4">
      <div className="flex flex-col gap-1.5">
      <label className={fieldLabelClass}>
        Recording platform
        <select
          value={selectedSessionPlatformKey(state, provider)}
          onChange={(event) => integrations.changeSessionPlatform(provider, event.target.value)}
          disabled={mutationInProgress}
          className={fieldInputClass}
        >
          {options.map((option) => (
            <option key={option.key} value={option.key}>
              {option.name}
            </option>
          ))}
        </select>
      </label>
      <span className="px-1 text-xs text-muted">New sessions recorded by this integration use this platform.</span>
      </div>

      {state.pendingSessionPlatformProviderId === provider.id ? (
        <div
          role="alertdialog"
          aria-label="Confirm recording platform change"
          className="mt-4 border-t border-divider pt-4"
        >
          <p className="text-[13px] text-ink-2">Should existing sessions from this integration also use the new platform?</p>
          <div className="mt-3 flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => void integrations.saveSessionPlatform(provider, true)}
              disabled={saving}
              className="pdx-btn-primary !h-[34px] !px-3.5 !text-xs"
            >
              Future and existing sessions
            </button>
            <button
              type="button"
              onClick={() => void integrations.saveSessionPlatform(provider, false)}
              disabled={saving}
              className="pdx-btn-outline !h-[34px] !px-3.5 !text-xs"
            >
              Future sessions only
            </button>
            <button
              type="button"
              onClick={integrations.cancelSessionPlatformChange}
              disabled={saving}
              className="pdx-btn !h-[34px] border-0 bg-transparent !px-3.5 !text-xs text-muted hover:text-ink disabled:opacity-60"
            >
              Cancel
            </button>
          </div>
        </div>
      ) : null}

      {state.sessionPlatformError ? <p className="mt-3 text-[13px] text-danger">{state.sessionPlatformError}</p> : null}
    </div>
  );
}
