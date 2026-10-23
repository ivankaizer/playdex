import { formatProviderName } from "@/lib/format";
import type { ProviderStatus, ProvidersResponse, Submission } from "../models";

/** Connected integrations: list, status toggles, credential editing and recording platform changes. */
export interface ProvidersState {
  providers: ProviderStatus[];
  isProvidersLoading: boolean;
  providersError: string;
  updatingProviderIds: ReadonlySet<number>;
  savingCredentialIds: ReadonlySet<number>;
  savingSessionPlatformIds: ReadonlySet<number>;
  editingCredentialId: number | null;
  editingAccountId: string;
  editingApiKey: string;
  credentialError: string;
  pendingSessionPlatformProviderId: number | null;
  selectedSessionPlatformKey: string;
  sessionPlatformError: string;
}

export interface ProviderPresentation {
  /** Short mark shown in the service tile, e.g. "S" for Steam. */
  label: string;
  name: string;
  subtitle: string;
}

export const providersPath = "/providers";

export class ProviderChangePendingError extends Error {}

export function createProvidersState(): ProvidersState {
  return {
    providers: [],
    isProvidersLoading: false,
    providersError: "",
    updatingProviderIds: new Set(),
    savingCredentialIds: new Set(),
    savingSessionPlatformIds: new Set(),
    editingCredentialId: null,
    editingAccountId: "",
    editingApiKey: "",
    credentialError: "",
    pendingSessionPlatformProviderId: null,
    selectedSessionPlatformKey: "",
    sessionPlatformError: "",
  };
}

export function visibleProviders(state: ProvidersState): ProviderStatus[] {
  return state.providers.filter((provider) => provider.platform !== "manual" && provider.platform !== "local");
}

export function isAnySessionPlatformSaving(state: ProvidersState): boolean {
  return state.savingSessionPlatformIds.size > 0;
}

export function isProviderUpdating(state: ProvidersState, providerId: number): boolean {
  return state.updatingProviderIds.has(providerId);
}

export function isProviderMutationInProgress(state: ProvidersState, providerId: number): boolean {
  return (
    state.updatingProviderIds.has(providerId) || state.savingCredentialIds.has(providerId) || state.savingSessionPlatformIds.has(providerId)
  );
}

export function isProviderActionBlocked(state: ProvidersState, providerId: number): boolean {
  return isProviderMutationInProgress(state, providerId) || state.pendingSessionPlatformProviderId === providerId;
}

export function isEditingCredential(state: ProvidersState, providerId: number): boolean {
  return state.editingCredentialId === providerId;
}

export function isCredentialSaving(state: ProvidersState, providerId: number): boolean {
  return state.savingCredentialIds.has(providerId);
}

export function isSessionPlatformSaving(state: ProvidersState, providerId: number): boolean {
  return state.savingSessionPlatformIds.has(providerId);
}

// Loading

export function beginProvidersLoad(state: ProvidersState): ProvidersState {
  return { ...state, isProvidersLoading: true, providersError: "" };
}

export function completeProvidersLoad(state: ProvidersState, response: ProvidersResponse): ProvidersState {
  return { ...state, providers: response.providers, isProvidersLoading: false };
}

export function failProvidersLoad(state: ProvidersState, error: string): ProvidersState {
  return { ...state, providers: [], isProvidersLoading: false, providersError: error };
}

// Status toggle

export function buildProviderUpdate(provider: ProviderStatus): Submission {
  return {
    path: "/providers/status",
    method: "PUT",
    body: { providerId: provider.id, status: provider.status === "active" ? "paused" : "active" },
  };
}

export function beginProviderUpdate(state: ProvidersState, providerId: number): ProvidersState {
  if (isProviderActionBlocked(state, providerId)) {
    throw new ProviderChangePendingError("Another provider change is already pending.");
  }
  return { ...state, updatingProviderIds: withId(state.updatingProviderIds, providerId), providersError: "" };
}

export function completeProviderUpdate(state: ProvidersState, updated: ProviderStatus): ProvidersState {
  return { ...state, providers: replaceProvider(state.providers, updated) };
}

export function failProviderUpdate(state: ProvidersState, error: string): ProvidersState {
  return { ...state, providersError: error };
}

export function endProviderUpdate(state: ProvidersState, providerId: number): ProvidersState {
  return { ...state, updatingProviderIds: withoutId(state.updatingProviderIds, providerId) };
}

// Credential editing

export function beginCredentialEdit(state: ProvidersState, provider: ProviderStatus): ProvidersState {
  if (isAnySessionPlatformSaving(state)) return state;
  const base = state.pendingSessionPlatformProviderId !== provider.id ? cancelSessionPlatformChange(state) : state;
  return {
    ...base,
    editingCredentialId: provider.id,
    editingAccountId: provider.accountId ?? "",
    editingApiKey: provider.apiKey ?? "",
    selectedSessionPlatformKey: provider.sessionPlatform?.key ?? "",
    credentialError: "",
  };
}

export function cancelCredentialEdit(state: ProvidersState): ProvidersState {
  return {
    ...cancelSessionPlatformChange(state),
    editingCredentialId: null,
    editingAccountId: "",
    editingApiKey: "",
    credentialError: "",
  };
}

export function setEditingAccountId(state: ProvidersState, value: string): ProvidersState {
  return { ...state, editingAccountId: value };
}

export function setEditingApiKey(state: ProvidersState, value: string): ProvidersState {
  return { ...state, editingApiKey: value };
}

export function buildCredentialSubmission(state: ProvidersState): {
  state: ProvidersState;
  submission: Submission | null;
} {
  const cleared = { ...state, credentialError: "" };
  const providerId = state.editingCredentialId;
  if (providerId === null) return { state: cleared, submission: null };
  const provider = state.providers.find((item) => item.id === providerId);
  if (!provider) return { state: cleared, submission: null };

  const noChanges = { state: { ...state, credentialError: "No changes to save." }, submission: null };
  const savedAccountId = provider.accountId ?? "";
  const savedApiKey = provider.apiKey ?? "";

  if (state.editingAccountId === savedAccountId && (provider.apiKeyLocked || state.editingApiKey === savedApiKey)) {
    return noChanges;
  }

  const trimmedAccountId = state.editingAccountId.trim();
  const trimmedApiKey = state.editingApiKey.trim();
  const accountIdChanged = trimmedAccountId !== savedAccountId;
  const apiKeyChanged = !provider.apiKeyLocked && trimmedApiKey !== savedApiKey;
  if (!accountIdChanged && !apiKeyChanged) return noChanges;

  const body: Record<string, unknown> = {};
  if (accountIdChanged) body.accountId = trimmedAccountId === "" ? null : trimmedAccountId;
  if (apiKeyChanged) body.apiKey = trimmedApiKey === "" ? null : trimmedApiKey;

  return { state: cleared, submission: { path: `/providers/${providerId}`, method: "PUT", body } };
}

export function beginCredentialSave(state: ProvidersState, providerId: number): ProvidersState {
  if (isProviderActionBlocked(state, providerId)) {
    throw new ProviderChangePendingError("Another provider change is already pending.");
  }
  return { ...state, savingCredentialIds: withId(state.savingCredentialIds, providerId), credentialError: "" };
}

export function completeCredentialSave(state: ProvidersState, updated: ProviderStatus): ProvidersState {
  return cancelSessionPlatformChange({
    ...state,
    providers: replaceProvider(state.providers, updated),
    editingCredentialId: null,
    editingAccountId: "",
    editingApiKey: "",
    savingCredentialIds: withoutId(state.savingCredentialIds, updated.id),
  });
}

export function failCredentialSave(state: ProvidersState, error: string): ProvidersState {
  return { ...state, credentialError: error };
}

export function endCredentialSave(state: ProvidersState, providerId: number): ProvidersState {
  return { ...state, savingCredentialIds: withoutId(state.savingCredentialIds, providerId) };
}

// Recording platform

export function selectSessionPlatform(state: ProvidersState, provider: ProviderStatus, platformKey: string): ProvidersState | null {
  if (isAnySessionPlatformSaving(state) || isProviderMutationInProgress(state, provider.id)) return null;
  if (!provider.sessionPlatformOptions?.some((option) => option.key === platformKey)) return null;

  return {
    ...state,
    selectedSessionPlatformKey: platformKey,
    sessionPlatformError: "",
    pendingSessionPlatformProviderId: platformKey === provider.sessionPlatform?.key ? null : provider.id,
  };
}

export function selectedSessionPlatformKey(state: ProvidersState, provider: ProviderStatus): string {
  return state.pendingSessionPlatformProviderId === provider.id ? state.selectedSessionPlatformKey : (provider.sessionPlatform?.key ?? "");
}

export function buildSessionPlatformSubmission(state: ProvidersState, rewriteExistingSessions: boolean): Submission {
  const providerId = state.pendingSessionPlatformProviderId;
  if (providerId === null || state.selectedSessionPlatformKey === "") {
    throw new ProviderChangePendingError("No session platform change is pending.");
  }
  return {
    path: `/providers/${providerId}/session-platform`,
    method: "PUT",
    body: { platform: state.selectedSessionPlatformKey, rewriteExistingSessions },
  };
}

export function cancelSessionPlatformChange(state: ProvidersState): ProvidersState {
  const providerId = state.pendingSessionPlatformProviderId;
  const provider = providerId !== null ? state.providers.find((item) => item.id === providerId) : undefined;
  return {
    ...state,
    selectedSessionPlatformKey: provider?.sessionPlatform?.key ?? "",
    pendingSessionPlatformProviderId: null,
    sessionPlatformError: "",
  };
}

export function beginSessionPlatformSave(state: ProvidersState, providerId: number): ProvidersState {
  if (isProviderMutationInProgress(state, providerId)) {
    throw new ProviderChangePendingError("Another provider change is already in progress.");
  }
  return {
    ...state,
    savingSessionPlatformIds: withId(state.savingSessionPlatformIds, providerId),
    sessionPlatformError: "",
  };
}

export function completeSessionPlatformSave(state: ProvidersState, updated: ProviderStatus): ProvidersState {
  return {
    ...state,
    providers: replaceProvider(state.providers, updated),
    selectedSessionPlatformKey: updated.sessionPlatform?.key ?? "",
    pendingSessionPlatformProviderId: null,
    sessionPlatformError: "",
  };
}

export function failSessionPlatformSave(state: ProvidersState, error: string): ProvidersState {
  return { ...state, sessionPlatformError: error };
}

export function endSessionPlatformSave(state: ProvidersState, providerId: number): ProvidersState {
  return { ...state, savingSessionPlatformIds: withoutId(state.savingSessionPlatformIds, providerId) };
}

export function appendProvider(state: ProvidersState, provider: ProviderStatus): ProvidersState {
  return { ...state, providers: [...state.providers, provider] };
}

// Presentation

export function presentProvider(provider: ProviderStatus): ProviderPresentation {
  const label = providerMark(provider.platform);
  const subtitle = provider.platform === "playnite" ? (provider.apiKey ?? "Local") : (provider.accountId ?? "Local");
  return { label, name: formatProviderName(provider.platform), subtitle };
}

/** Why Playdex paused this integration itself, with what to do about it; null when it did not. */
export function pauseNotice(provider: ProviderStatus): string | null {
  const reason = provider.pausedReason?.trim();
  if (provider.status !== "paused" || !reason) return null;
  const sentence = /[.!?]$/.test(reason) ? reason : `${reason}.`;
  return `Paused automatically: ${sentence} Update the credential under Manage, then resume.`;
}

/** Keeps both ends of a long account ID ("name … (7656…)") instead of cutting the tail off. */
export function truncateMiddle(value: string, max = 34): string {
  const chars = Array.from(value);
  if (chars.length <= max) return value;
  const head = Math.ceil((max - 1) * 0.6);
  return `${chars.slice(0, head).join("")}…${chars.slice(chars.length - (max - 1 - head)).join("")}`;
}

function providerMark(platform: string): string {
  switch (platform) {
    case "steam":
      return "S";
    case "psn":
      return "P";
    case "playnite":
      return "PL";
    case "xbox":
      return "X";
    default:
      return "PX";
  }
}

function replaceProvider(providers: ProviderStatus[], updated: ProviderStatus): ProviderStatus[] {
  return providers.map((provider) => (provider.id === updated.id ? updated : provider));
}

function withId(ids: ReadonlySet<number>, id: number): ReadonlySet<number> {
  return new Set(ids).add(id);
}

function withoutId(ids: ReadonlySet<number>, id: number): ReadonlySet<number> {
  const next = new Set(ids);
  next.delete(id);
  return next;
}
