import { useCallback, useEffect, useRef } from "react";
import { apiRequest, errorMessage, isAbortError } from "@/lib/api";
import type {
  ProviderCreateResponse,
  ProviderCredentialUpdateResponse,
  ProviderSessionPlatformUpdateResponse,
  ProviderStatus,
  ProvidersResponse,
  ProviderUpdateResponse,
} from "../models";
import { LatestRequest } from "../shared/latestRequest";
import { isLifetimeCancellation } from "../shared/lifetime";
import { useStore } from "../shared/useStore";
import * as creator from "./creatorState";
import * as providers from "./providersState";

// Blazor begins the initial load before the first render, so the first paint already shows loading.
const initialProvidersState = () => providers.beginProvidersLoad(providers.createProvidersState());

/** Connected services: provider list, per-provider management and the integration creator. */
export function useIntegrations() {
  const [state, update, get] = useStore(initialProvidersState);
  const [creatorState, updateCreator, getCreator] = useStore(creator.createCreatorState);
  const lifetime = useRef(new AbortController());
  const providersRequest = useRef(new LatestRequest());

  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    const request = providersRequest.current;
    return () => {
      controller.abort();
      request.invalidate();
    };
  }, []);

  const loadProviders = useCallback(async () => {
    update(providers.beginProvidersLoad);
    const request = providersRequest.current.begin();
    try {
      const response = await apiRequest<ProvidersResponse>(providers.providersPath, { signal: request.signal });
      if (request.isCurrent() && !request.signal.aborted) {
        update((current) => providers.completeProvidersLoad(current, response));
      }
    } catch (error) {
      if (isAbortError(error) && request.signal.aborted) return;
      if (request.isCurrent()) update((current) => providers.failProvidersLoad(current, errorMessage(error)));
    } finally {
      request.finish();
    }
  }, [update]);

  const toggleProvider = useCallback(
    async (provider: ProviderStatus) => {
      // The button is disabled while blocked; Blazor threw here, React simply ignores the click.
      if (providers.isProviderActionBlocked(get(), provider.id)) return;
      const submission = providers.buildProviderUpdate(provider);
      update((current) => providers.beginProviderUpdate(current, provider.id));
      const signal = lifetime.current.signal;
      try {
        const response = await apiRequest<ProviderUpdateResponse>(submission.path, {
          method: submission.method,
          body: submission.body,
          signal,
        });
        update((current) => providers.completeProviderUpdate(current, response.provider));
      } catch (error) {
        if (!isLifetimeCancellation(error, signal)) {
          update((current) => providers.failProviderUpdate(current, errorMessage(error)));
        }
      } finally {
        update((current) => providers.endProviderUpdate(current, provider.id));
      }
    },
    [get, update],
  );

  const toggleCredentialEditor = useCallback(
    (provider: ProviderStatus) =>
      update((current) =>
        providers.isEditingCredential(current, provider.id)
          ? providers.cancelCredentialEdit(current)
          : providers.beginCredentialEdit(current, provider),
      ),
    [update],
  );

  const setEditingAccountId = useCallback((value: string) => update((current) => providers.setEditingAccountId(current, value)), [update]);

  const setEditingApiKey = useCallback((value: string) => update((current) => providers.setEditingApiKey(current, value)), [update]);

  const saveCredential = useCallback(
    async (provider: ProviderStatus) => {
      const { state: validated, submission } = providers.buildCredentialSubmission(get());
      update(() => validated);
      if (!submission) return;
      if (providers.isProviderActionBlocked(get(), provider.id)) return;

      update((current) => providers.beginCredentialSave(current, provider.id));
      const signal = lifetime.current.signal;
      try {
        const response = await apiRequest<ProviderCredentialUpdateResponse>(submission.path, {
          method: submission.method,
          body: submission.body,
          signal,
        });
        update((current) => providers.completeCredentialSave(current, response.provider));
      } catch (error) {
        if (!isLifetimeCancellation(error, signal)) {
          update((current) => providers.failCredentialSave(current, errorMessage(error)));
        }
      } finally {
        update((current) => providers.endCredentialSave(current, provider.id));
      }
    },
    [get, update],
  );

  const changeSessionPlatform = useCallback(
    (provider: ProviderStatus, platformKey: string) =>
      update((current) => providers.selectSessionPlatform(current, provider, platformKey) ?? current),
    [update],
  );

  const cancelSessionPlatformChange = useCallback(() => update(providers.cancelSessionPlatformChange), [update]);

  const saveSessionPlatform = useCallback(
    async (provider: ProviderStatus, rewriteExistingSessions: boolean) => {
      const current = get();
      if (current.pendingSessionPlatformProviderId === null || current.selectedSessionPlatformKey === "") return;
      if (providers.isProviderMutationInProgress(current, provider.id)) return;
      const submission = providers.buildSessionPlatformSubmission(current, rewriteExistingSessions);

      update((latest) => providers.beginSessionPlatformSave(latest, provider.id));
      const signal = lifetime.current.signal;
      try {
        const response = await apiRequest<ProviderSessionPlatformUpdateResponse>(submission.path, {
          method: submission.method,
          body: submission.body,
          signal,
        });
        update((latest) => providers.completeSessionPlatformSave(latest, response.provider));
      } catch (error) {
        if (!isLifetimeCancellation(error, signal)) {
          update((latest) => providers.failSessionPlatformSave(latest, errorMessage(error)));
        }
      } finally {
        update((latest) => providers.endSessionPlatformSave(latest, provider.id));
      }
    },
    [get, update],
  );

  const openIntegrationCreator = useCallback(
    () => updateCreator((current) => creator.openIntegrationCreator(current, get().providers)),
    [get, updateCreator],
  );

  const selectCreationPlatform = useCallback(
    (platform: creator.CreationPlatform) =>
      updateCreator((current) => creator.selectCreationPlatform(current, get().providers, platform) ?? current),
    [get, updateCreator],
  );

  const cancelIntegrationCreation = useCallback(() => updateCreator(creator.cancelIntegrationCreation), [updateCreator]);

  const setCreationApiKey = useCallback(
    (value: string) => updateCreator((current) => creator.setCreationApiKey(current, value)),
    [updateCreator],
  );

  const setCreationAccountId = useCallback(
    (value: string) => updateCreator((current) => creator.setCreationAccountId(current, value)),
    [updateCreator],
  );

  const createIntegration = useCallback(async () => {
    const { state: validated, submission } = creator.buildIntegrationCreationSubmission(getCreator());
    updateCreator(() => validated);
    if (!submission) return;

    updateCreator(creator.beginIntegrationCreation);
    const signal = lifetime.current.signal;
    try {
      const response = await apiRequest<ProviderCreateResponse>(submission.path, {
        method: submission.method,
        body: submission.body,
        signal,
      });
      update((current) => providers.appendProvider(current, response.provider));
      updateCreator(creator.completeIntegrationCreation);
    } catch (error) {
      if (!isLifetimeCancellation(error, signal)) {
        updateCreator((current) => creator.failIntegrationCreation(current, errorMessage(error)));
      }
    } finally {
      updateCreator(creator.endIntegrationCreation);
    }
  }, [getCreator, update, updateCreator]);

  return {
    state,
    creatorState,
    loadProviders,
    toggleProvider,
    toggleCredentialEditor,
    setEditingAccountId,
    setEditingApiKey,
    saveCredential,
    changeSessionPlatform,
    cancelSessionPlatformChange,
    saveSessionPlatform,
    openIntegrationCreator,
    selectCreationPlatform,
    cancelIntegrationCreation,
    setCreationApiKey,
    setCreationAccountId,
    createIntegration,
  };
}

export type Integrations = ReturnType<typeof useIntegrations>;
