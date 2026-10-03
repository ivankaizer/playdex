import { creationPlatformName, type CreationPlatform } from "./creatorState";
import type { Integrations } from "./useIntegrations";

interface IntegrationCreatorProps {
  available: readonly CreationPlatform[];
  integrations: Integrations;
}

const platformButtonClass =
  "rounded-xl border border-slate-300 bg-white/80 px-4 py-3 text-left text-sm font-semibold text-slate-900 transition hover:border-emerald-400 dark:border-slate-700 dark:bg-slate-900/70 dark:text-white";

const inputClass =
  "mt-2 w-full rounded-xl border border-slate-300 bg-white/90 px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:bg-slate-950/80 dark:text-white";

export function IntegrationCreator({ available, integrations }: IntegrationCreatorProps) {
  const state = integrations.creatorState;
  const platform = state.selectedCreationPlatform;

  return (
    <div
      data-integration-creator=""
      className="mt-6 rounded-2xl border border-dashed border-slate-300 bg-slate-100/40 p-4 dark:border-slate-700 dark:bg-slate-950/30 sm:p-5"
    >
      {!state.isIntegrationCreatorOpen ? (
        <button
          type="button"
          onClick={integrations.openIntegrationCreator}
          className="flex w-full items-center justify-between gap-4 text-left"
        >
          <span>
            <span className="block text-sm font-semibold text-slate-900 dark:text-white">Add integration</span>
            <span className="mt-1 block text-xs text-slate-500 dark:text-slate-400">
              Connect up to 3 accounts per service.
            </span>
          </span>
          <span aria-hidden="true" className="text-xl text-emerald-600 dark:text-emerald-300">
            +
          </span>
        </button>
      ) : platform === null ? (
        <>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="text-sm font-semibold text-slate-900 dark:text-white">Choose an integration</p>
              <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">
                You can connect up to 3 accounts per service.
              </p>
            </div>
            <button
              type="button"
              onClick={integrations.cancelIntegrationCreation}
              className="text-xs font-semibold text-slate-500 hover:text-slate-900 dark:text-slate-400 dark:hover:text-white"
            >
              Cancel
            </button>
          </div>
          <div className="mt-4 grid gap-3 sm:grid-cols-3">
            {available.includes("steam") ? (
              <button type="button" onClick={() => integrations.selectCreationPlatform("steam")} className={platformButtonClass}>
                Steam
              </button>
            ) : null}
            {available.includes("psn") ? (
              <button type="button" onClick={() => integrations.selectCreationPlatform("psn")} className={platformButtonClass}>
                PlayStation
              </button>
            ) : null}
            {available.includes("xbox") ? (
              <button type="button" onClick={() => integrations.selectCreationPlatform("xbox")} className={platformButtonClass}>
                Xbox
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <>
          <div>
            <p className="text-sm font-semibold text-slate-900 dark:text-white">
              Connect {creationPlatformName(platform)}
            </p>
            <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">
              Enter the credentials Playdex needs to sync this account.
            </p>
          </div>

          <div className="mt-5 grid gap-3 sm:grid-cols-2">
            <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
              <span className="block">
                {platform === "steam" ? "Steam Web API key" : platform === "psn" ? "NPSSO token" : "OpenXBL API key"}
              </span>
              <input
                type="password"
                value={state.creationApiKey}
                onChange={(event) => integrations.setCreationApiKey(event.target.value)}
                disabled={state.isCreatingIntegration}
                autoComplete="off"
                className={inputClass}
              />
            </label>
            <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
              <span className="block">
                {platform === "steam" ? "Steam ID" : platform === "psn" ? "Account ID" : "Xbox XUID"}
                {platform !== "steam" ? <span className="font-normal text-slate-400"> (optional)</span> : null}
              </span>
              <input
                type="text"
                value={state.creationAccountId}
                onChange={(event) => integrations.setCreationAccountId(event.target.value)}
                disabled={state.isCreatingIntegration}
                inputMode={platform === "xbox" ? "numeric" : undefined}
                className={inputClass}
              />
            </label>
          </div>
          {platform === "xbox" ? (
            <p className="mt-3 text-[11px] text-slate-500 dark:text-slate-400">
              Leave XUID empty to use the Xbox account linked to the key. Get a key from{" "}
              <a
                href="https://xbl.io/dashboard"
                target="_blank"
                rel="noreferrer"
                className="font-semibold text-emerald-700 underline dark:text-emerald-300"
              >
                OpenXBL
              </a>
              .
            </p>
          ) : null}
          {state.creationError ? (
            <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">{state.creationError}</p>
          ) : null}
          <div className="mt-4 flex flex-wrap justify-end gap-3">
            <button
              type="button"
              onClick={integrations.cancelIntegrationCreation}
              disabled={state.isCreatingIntegration}
              className="rounded-full border border-slate-300 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-600 transition hover:border-slate-400 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:text-slate-300"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={() => void integrations.createIntegration()}
              disabled={state.isCreatingIntegration}
              className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {state.isCreatingIntegration ? "Connecting..." : `Connect ${creationPlatformName(platform)}`}
            </button>
          </div>
        </>
      )}
    </div>
  );
}
