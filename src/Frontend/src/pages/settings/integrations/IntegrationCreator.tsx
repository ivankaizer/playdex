import { platformColor } from "@/lib/platforms";
import {
  availableIntegrationPlatforms,
  creationPlatformName,
  MaxIntegrationsPerPlatform,
  SupportedCreationPlatforms,
  type CreationPlatform,
} from "./creatorState";
import type { Integrations } from "./useIntegrations";

interface IntegrationCreatorProps {
  integrations: Integrations;
}

const serviceMarks: Record<CreationPlatform, string> = { steam: "S", psn: "PS", xbox: "X" };

const fieldLabelClass = "pdx-label flex min-w-0 flex-col gap-2";
const fieldInputClass = "pdx-input !h-12 !px-5 tracking-normal";

export function IntegrationCreator({ integrations }: IntegrationCreatorProps) {
  const state = integrations.creatorState;
  const providers = integrations.state.providers;
  const platform = state.selectedCreationPlatform;
  const available = availableIntegrationPlatforms(providers);
  const open = state.isIntegrationCreatorOpen;
  const allFull = available.length === 0;

  const toggle = open ? integrations.cancelIntegrationCreation : integrations.openIntegrationCreator;

  return (
    <div data-integration-creator="" className="flex flex-col gap-3.5 rounded-tile border-[1.5px] border-dashed border-dash px-5 py-[18px]">
      <button
        type="button"
        onClick={toggle}
        disabled={(!open && allFull) || state.isCreatingIntegration}
        aria-expanded={open}
        aria-label={open ? "Close add integration" : "Add integration"}
        className="flex w-full items-center justify-between gap-3 text-left disabled:cursor-not-allowed disabled:opacity-60"
      >
        <span>
          <span className="block text-[15px] font-semibold">
            {platform !== null ? `Connect ${creationPlatformName(platform)}` : "Add integration"}
          </span>
          <span className="mt-0.5 block text-[13px] text-muted">
            {allFull && !open
              ? "Every service already has 3 accounts connected."
              : platform !== null
                ? "Enter the credentials Playdex needs to sync this account."
                : `Up to ${MaxIntegrationsPerPlatform} accounts per service`}
          </span>
        </span>
        <span
          aria-hidden="true"
          className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-pill text-xl font-semibold text-pill-ink"
        >
          {open ? "×" : "+"}
        </span>
      </button>

      {open && platform === null ? (
        <div className="flex flex-wrap gap-2.5" role="group" aria-label="Choose an integration">
          {SupportedCreationPlatforms.map((key) => {
            const count = providers.filter((provider) => provider.platform === key).length;
            const full = !available.includes(key);
            const name = creationPlatformName(key);
            return (
              <button
                key={key}
                type="button"
                onClick={() => integrations.selectCreationPlatform(key)}
                disabled={full}
                aria-label={name}
                className="flex h-12 items-center gap-2.5 rounded-full border border-line bg-surface pl-2 pr-[18px] text-sm font-semibold text-ink transition-colors hover:border-mint-hover disabled:cursor-not-allowed disabled:opacity-45 disabled:hover:border-line"
              >
                <span
                  aria-hidden="true"
                  className="grid h-8 w-8 place-items-center rounded-full text-[11px] font-bold text-white"
                  style={{ background: platformColor(key, name) }}
                >
                  {serviceMarks[key]}
                </span>
                {name}
                <span aria-hidden="true" className="pdx-num text-xs font-normal text-muted">
                  {count} of {MaxIntegrationsPerPlatform}
                </span>
              </button>
            );
          })}
        </div>
      ) : null}

      {open && platform !== null ? (
        <div className="flex flex-col gap-4">
          <div className="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(min(260px,100%),1fr))]">
            <label className={fieldLabelClass}>
              {platform === "steam" ? "Steam Web API key" : platform === "psn" ? "NPSSO token" : "OpenXBL API key"}
              <input
                type="password"
                value={state.creationApiKey}
                onChange={(event) => integrations.setCreationApiKey(event.target.value)}
                disabled={state.isCreatingIntegration}
                autoComplete="off"
                className={fieldInputClass}
              />
            </label>
            <label className={fieldLabelClass}>
              <span>
                {platform === "steam" ? "Steam ID" : platform === "psn" ? "Account ID" : "Xbox XUID"}
                {platform !== "steam" ? <span className="font-normal normal-case tracking-normal text-muted"> (optional)</span> : null}
              </span>
              <input
                type="text"
                value={state.creationAccountId}
                onChange={(event) => integrations.setCreationAccountId(event.target.value)}
                disabled={state.isCreatingIntegration}
                inputMode={platform === "xbox" ? "numeric" : undefined}
                className={fieldInputClass}
              />
            </label>
          </div>
          {platform === "xbox" ? (
            <p className="text-xs text-muted">
              Leave XUID empty to use the Xbox account linked to the key. Get a key from{" "}
              <a href="https://xbl.io/dashboard" target="_blank" rel="noreferrer" className="font-semibold text-accent underline">
                OpenXBL
              </a>
              .
            </p>
          ) : null}
          {state.creationError ? <p role="alert" className="text-[13px] text-danger">{state.creationError}</p> : null}
          <div className="flex flex-wrap justify-end gap-2.5">
            <button
              type="button"
              onClick={integrations.cancelIntegrationCreation}
              disabled={state.isCreatingIntegration}
              className="pdx-btn-outline !h-11"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={() => void integrations.createIntegration()}
              disabled={state.isCreatingIntegration}
              className="pdx-btn-primary !h-11"
            >
              {state.isCreatingIntegration ? "Connecting..." : `Connect ${creationPlatformName(platform)}`}
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
