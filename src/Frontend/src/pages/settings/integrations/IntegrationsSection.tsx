import { SectionHeading } from "../shared/SectionHeading";
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
  const activeCount = visible.filter((provider) => provider.status === "active").length;

  return (
    <section data-settings-section="integrations" className="pdx-card flex flex-col gap-6">
      <SectionHeading
        kicker="Integrations"
        title="Connected services"
        description="Accounts that feed activity into Playdex."
        aside={
          !state.isProvidersLoading && visible.length > 0 ? (
            <span className="pdx-num text-[13px] text-muted">
              {activeCount} of {visible.length} active
            </span>
          ) : null
        }
      />

      {state.providersError ? (
        <p role="alert" className="rounded-[18px] border border-danger-line bg-danger-tint px-4 py-3 text-[13px] text-danger">
          {state.providersError}
        </p>
      ) : null}

      <div className="flex flex-col gap-2.5">
        {state.isProvidersLoading ? (
          [1, 2].map((item) => (
            <div
              key={item}
              aria-hidden="true"
              className="grid grid-cols-[48px_minmax(0,1fr)_auto] items-center gap-4 rounded-tile border border-[var(--tile-border)] bg-tile px-5 py-4"
            >
              <div className="h-12 w-12 motion-safe:animate-pulse rounded-[14px] bg-track"></div>
              <div className="space-y-2">
                <div className="h-3 w-28 motion-safe:animate-pulse rounded-full bg-track"></div>
                <div className="h-2.5 w-40 max-w-full motion-safe:animate-pulse rounded-full bg-track"></div>
              </div>
              <div className="h-9 w-24 motion-safe:animate-pulse rounded-full bg-track"></div>
            </div>
          ))
        ) : visible.length === 0 ? (
          <div className="pdx-dashed !rounded-[20px] !p-5 !text-sm">
            No integrations connected yet. Add one to start syncing playtime.
          </div>
        ) : (
          visible.map((provider) => <IntegrationCard key={provider.id} provider={provider} integrations={integrations} />)
        )}

        {!state.isProvidersLoading ? <IntegrationCreator integrations={integrations} /> : null}
      </div>
    </section>
  );
}
