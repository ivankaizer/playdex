import { availableIntegrationPlatforms } from "./creatorState";
import { IntegrationCard } from "./IntegrationCard";
import { IntegrationCreator } from "./IntegrationCreator";
import { visibleProviders } from "./providersState";
import type { Integrations } from "./useIntegrations";

interface IntegrationsSectionProps {
  integrations: Integrations;
}

export function IntegrationsSection({ integrations }: IntegrationsSectionProps) {
  const { state } = integrations;
  const visible = visibleProviders(state);
  const available = availableIntegrationPlatforms(state.providers);

  return (
    <section
      data-settings-section="integrations"
      className="rounded-3xl border border-slate-200 bg-white/80 p-5 dark:border-slate-800 dark:bg-slate-900/60 sm:p-6"
    >
      <div>
        <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
          Integrations
        </p>
        <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Connected services</h2>
        <p className="mt-2 text-xs text-slate-500 dark:text-slate-400">
          Manage the services that contribute activity to Playdex.
        </p>
      </div>

      {state.providersError ? (
        <p className="mt-4 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2.5 text-xs text-rose-700 dark:border-rose-900/60 dark:bg-rose-950/30 dark:text-rose-300">
          {state.providersError}
        </p>
      ) : null}

      <div className="mt-6 space-y-3">
        {state.isProvidersLoading ? (
          [1, 2].map((item) => (
            <div
              key={item}
              className="flex items-center justify-between gap-4 rounded-2xl border border-slate-200 bg-slate-100/60 p-4 dark:border-slate-800 dark:bg-slate-950/50"
            >
              <div className="flex items-center gap-3">
                <div className="h-10 w-10 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
                <div className="space-y-2">
                  <div className="h-3 w-24 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
                  <div className="h-2 w-16 rounded-full bg-slate-200/50 dark:bg-slate-800/50"></div>
                </div>
              </div>
              <div className="h-8 w-24 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
            </div>
          ))
        ) : visible.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-slate-300 bg-slate-100/50 p-4 text-xs text-slate-500 dark:border-slate-700 dark:bg-slate-950/40 dark:text-slate-400">
            No integrations connected yet. Add one to start syncing playtime.
          </div>
        ) : (
          visible.map((provider) => (
            <IntegrationCard key={provider.id} provider={provider} integrations={integrations} />
          ))
        )}
      </div>

      {!state.isProvidersLoading && available.length > 0 ? (
        <IntegrationCreator available={available} integrations={integrations} />
      ) : null}
    </section>
  );
}
