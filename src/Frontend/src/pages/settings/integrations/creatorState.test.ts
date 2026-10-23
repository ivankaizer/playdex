import { describe, expect, it } from "vitest";
import type { ProviderStatus } from "../models";
import * as c from "./creatorState";

function provider(id: number, platform: string): ProviderStatus {
  return { id, platform, accountId: `account-${id}`, status: "active", apiKey: null, apiKeyLocked: false };
}

function openCreator(providers: ProviderStatus[] = []) {
  return c.openIntegrationCreator(c.createCreatorState(), providers);
}

describe("creatorState", () => {
  it("is closed by default and offers platforms below their limit in order", () => {
    const state = c.createCreatorState();
    expect(state.isIntegrationCreatorOpen).toBe(false);
    expect(state.selectedCreationPlatform).toBeNull();
    expect(c.availableIntegrationPlatforms([provider(1, "steam"), provider(2, "api")])).toEqual(["steam", "psn", "xbox"]);
  });

  it.each([
    [0, true],
    [1, true],
    [2, true],
    [3, false],
  ])("offers a service until its third account (%i steam)", (count, available) => {
    const providers = Array.from({ length: count }, (_, index) => provider(index + 1, "steam"));
    const platforms = c.availableIntegrationPlatforms(providers);
    expect(platforms.includes("steam")).toBe(available);
    expect(platforms).toContain("psn");
    expect(platforms).toContain("xbox");
  });

  it("does not open when every platform is full and rejects unavailable selections", () => {
    const full = ["steam", "psn", "xbox"].flatMap((platform, p) => [1, 2, 3].map((n) => provider(p * 10 + n, platform)));
    expect(openCreator(full).isIntegrationCreatorOpen).toBe(false);
    const steamFull = [1, 2, 3].map((n) => provider(n, "steam"));
    expect(c.selectCreationPlatform(openCreator(steamFull), steamFull, "steam")).toBeNull();
    expect(c.selectCreationPlatform(c.createCreatorState(), [], "psn")).toBeNull();
  });

  it("selection and cancellation clear the local draft", () => {
    let state = openCreator();
    expect(state.isIntegrationCreatorOpen).toBe(true);
    state = c.selectCreationPlatform(state, [], "xbox")!;
    state = c.setCreationAccountId(c.setCreationApiKey(state, "openxbl-key"), "invalid-xuid");
    const result = c.buildIntegrationCreationSubmission(state);
    expect(result.submission).toBeNull();
    expect(result.state.creationError).toBe("Xbox XUID must contain digits only.");

    state = c.cancelIntegrationCreation(result.state);
    expect(state).toMatchObject({
      isIntegrationCreatorOpen: false,
      selectedCreationPlatform: null,
      creationApiKey: "",
      creationAccountId: "",
      creationError: "",
    });
  });

  it.each([
    ["steam", "Enter a Steam Web API key."],
    ["psn", "Enter a PlayStation NPSSO token."],
    ["xbox", "Enter an OpenXBL API key."],
  ] as const)("requires an api key for %s", (platform, message) => {
    const state = c.selectCreationPlatform(openCreator(), [], platform)!;
    expect(c.buildIntegrationCreationSubmission(state).state.creationError).toBe(message);
  });

  it("requires a Steam ID", () => {
    const state = c.setCreationApiKey(c.selectCreationPlatform(openCreator(), [], "steam")!, "key");
    expect(c.buildIntegrationCreationSubmission(state).state.creationError).toBe("Enter a Steam ID.");
  });

  it.each([
    ["steam", " steam-key ", " steam-id ", "steam-key", "steam-id"],
    ["psn", " npsso-token ", " ", "npsso-token", null],
    ["xbox", " openxbl-key ", " 2533274817903789 ", "openxbl-key", "2533274817903789"],
  ] as const)("builds the %s submission", (platform, apiKey, accountId, expectedApiKey, expectedAccountId) => {
    let state = c.selectCreationPlatform(openCreator(), [], platform)!;
    state = c.setCreationAccountId(c.setCreationApiKey(state, apiKey), accountId);
    const { submission } = c.buildIntegrationCreationSubmission(state);
    expect(submission).toEqual({
      path: "/providers",
      method: "POST",
      body: { platform, apiKey: expectedApiKey, accountId: expectedAccountId },
    });
    expect(Object.keys(submission!.body)).toEqual(["platform", "apiKey", "accountId"]);
  });

  it("successful creation closes the creator", () => {
    const providers = [provider(1, "steam"), provider(2, "xbox"), provider(3, "xbox")];
    let state = c.selectCreationPlatform(openCreator(providers), providers, "xbox")!;
    state = c.beginIntegrationCreation(c.setCreationApiKey(state, "openxbl-key"));
    expect(state.isCreatingIntegration).toBe(true);
    state = c.endIntegrationCreation(c.completeIntegrationCreation(state));
    const after = [...providers, provider(4, "xbox")];
    expect(c.availableIntegrationPlatforms(after)).toEqual(["steam", "psn"]);
    expect(state.isIntegrationCreatorOpen).toBe(false);
    expect(state.isCreatingIntegration).toBe(false);
    expect(state.selectedCreationPlatform).toBeNull();
    expect(state.creationApiKey).toBe("");
  });

  it("names creation platforms", () => {
    expect(["steam", "psn", "xbox"].map(c.creationPlatformName)).toEqual(["Steam", "PlayStation", "Xbox"]);
  });
});
