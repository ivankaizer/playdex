import type { ProviderStatus, Submission } from "../models";

/** The collapsed "Add integration" creator. Availability depends on the current provider list. */
export interface CreatorState {
  isIntegrationCreatorOpen: boolean;
  selectedCreationPlatform: CreationPlatform | null;
  creationApiKey: string;
  creationAccountId: string;
  creationError: string;
  isCreatingIntegration: boolean;
}

export type CreationPlatform = "steam" | "psn" | "xbox";

export const MaxIntegrationsPerPlatform = 3;
export const SupportedCreationPlatforms: readonly CreationPlatform[] = ["steam", "psn", "xbox"];

export function createCreatorState(): CreatorState {
  return {
    isIntegrationCreatorOpen: false,
    selectedCreationPlatform: null,
    creationApiKey: "",
    creationAccountId: "",
    creationError: "",
    isCreatingIntegration: false,
  };
}

export function availableIntegrationPlatforms(providers: readonly ProviderStatus[]): CreationPlatform[] {
  return SupportedCreationPlatforms.filter(
    (platform) => providers.filter((provider) => provider.platform === platform).length < MaxIntegrationsPerPlatform,
  );
}

export function creationPlatformName(platform: string): string {
  switch (platform) {
    case "steam":
      return "Steam";
    case "psn":
      return "PlayStation";
    default:
      return "Xbox";
  }
}

export function openIntegrationCreator(state: CreatorState, providers: readonly ProviderStatus[]): CreatorState {
  if (availableIntegrationPlatforms(providers).length === 0) return state;
  return { ...state, isIntegrationCreatorOpen: true, creationError: "" };
}

export function selectCreationPlatform(
  state: CreatorState,
  providers: readonly ProviderStatus[],
  platform: CreationPlatform,
): CreatorState | null {
  if (!state.isIntegrationCreatorOpen || !availableIntegrationPlatforms(providers).includes(platform)) return null;
  return { ...state, selectedCreationPlatform: platform, creationApiKey: "", creationAccountId: "", creationError: "" };
}

export function cancelIntegrationCreation(state: CreatorState): CreatorState {
  return resetIntegrationCreator(state);
}

export function setCreationApiKey(state: CreatorState, value: string): CreatorState {
  return { ...state, creationApiKey: value };
}

export function setCreationAccountId(state: CreatorState, value: string): CreatorState {
  return { ...state, creationAccountId: value };
}

export function buildIntegrationCreationSubmission(state: CreatorState): {
  state: CreatorState;
  submission: Submission | null;
} {
  const cleared = { ...state, creationError: "" };
  const platform = state.selectedCreationPlatform;
  if (platform === null) return { state: cleared, submission: null };

  const fail = (creationError: string) => ({ state: { ...state, creationError }, submission: null });

  const apiKey = state.creationApiKey.trim();
  if (apiKey.length === 0) {
    return fail(
      platform === "steam"
        ? "Enter a Steam Web API key."
        : platform === "psn"
          ? "Enter a PlayStation NPSSO token."
          : "Enter an OpenXBL API key.",
    );
  }

  const accountId = state.creationAccountId.trim();
  if (platform === "steam" && accountId.length === 0) return fail("Enter a Steam ID.");
  if (platform === "xbox" && !/^[0-9]*$/.test(accountId)) return fail("Xbox XUID must contain digits only.");

  return {
    state: cleared,
    submission: {
      path: "/providers",
      method: "POST",
      body: { platform, apiKey, accountId: accountId.length === 0 ? null : accountId },
    },
  };
}

export function beginIntegrationCreation(state: CreatorState): CreatorState {
  return { ...state, isCreatingIntegration: true, creationError: "" };
}

export function completeIntegrationCreation(state: CreatorState): CreatorState {
  return resetIntegrationCreator(state);
}

export function failIntegrationCreation(state: CreatorState, error: string): CreatorState {
  return { ...state, creationError: error };
}

export function endIntegrationCreation(state: CreatorState): CreatorState {
  return { ...state, isCreatingIntegration: false };
}

function resetIntegrationCreator(state: CreatorState): CreatorState {
  return {
    ...state,
    isIntegrationCreatorOpen: false,
    selectedCreationPlatform: null,
    creationApiKey: "",
    creationAccountId: "",
    creationError: "",
  };
}
