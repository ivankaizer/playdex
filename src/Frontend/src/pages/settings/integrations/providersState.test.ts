import { describe, expect, it } from "vitest";
import type { ProviderStatus } from "../models";
import * as s from "./providersState";

function provider(id: number, platform: string, status: string): ProviderStatus {
  return {
    id,
    platform,
    accountId: `account-${id}`,
    status,
    hasApiKey: true,
    apiKeyLocked: false,
  };
}

function providerWithPlatforms(id: number): ProviderStatus {
  return {
    ...provider(id, "psn", "active"),
    sessionPlatform: { key: "psn", name: "PSN" },
    sessionPlatformOptions: [
      { key: "psn", name: "PSN" },
      { key: "ps5", name: "PS5" },
    ],
  };
}

function loaded(...providers: ProviderStatus[]) {
  return s.completeProvidersLoad(s.createProvidersState(), { providers });
}

describe("providersState", () => {
  it.each([true, false])("builds an explicit session platform rewrite (%s)", (rewrite) => {
    const p = providerWithPlatforms(7);
    const state = s.selectSessionPlatform(loaded(p), p, "ps5");
    expect(state).not.toBeNull();
    const submission = s.buildSessionPlatformSubmission(state!, rewrite);
    expect(submission).toEqual({
      path: "/providers/7/session-platform",
      method: "PUT",
      body: { platform: "ps5", rewriteExistingSessions: rewrite },
    });
  });

  it("cancelling a session platform change restores the effective platform", () => {
    const p = providerWithPlatforms(7);
    const state = s.cancelSessionPlatformChange(s.selectSessionPlatform(loaded(p), p, "ps5")!);
    expect(state.pendingSessionPlatformProviderId).toBeNull();
    expect(state.selectedSessionPlatformKey).toBe("psn");
  });

  it("closing management cancels a pending session platform change", () => {
    const p = providerWithPlatforms(7);
    let state = s.beginCredentialEdit(loaded(p), p);
    state = s.cancelCredentialEdit(s.selectSessionPlatform(state, p, "ps5")!);
    expect(state.pendingSessionPlatformProviderId).toBeNull();
    expect(state.selectedSessionPlatformKey).toBe("psn");
  });

  it("switching managed integration cancels the previous platform draft", () => {
    const first = providerWithPlatforms(7);
    const second = providerWithPlatforms(8);
    let state = s.beginCredentialEdit(loaded(first, second), first);
    state = s.selectSessionPlatform(state, first, "ps5")!;
    state = s.beginCredentialEdit(state, second);
    expect(state.editingCredentialId).toBe(8);
    expect(state.pendingSessionPlatformProviderId).toBeNull();
    expect(state.selectedSessionPlatformKey).toBe("psn");
  });

  it("an in-flight platform save prevents another integration from replacing its draft", () => {
    const first = providerWithPlatforms(7);
    const second = providerWithPlatforms(8);
    let state = s.beginCredentialEdit(loaded(first, second), first);
    state = s.selectSessionPlatform(state, first, "ps5")!;
    state = s.beginSessionPlatformSave(state, first.id);
    state = s.beginCredentialEdit(state, second);
    expect(state.editingCredentialId).toBe(7);
    expect(s.selectSessionPlatform(state, second, "ps5")).toBeNull();
    expect(state.pendingSessionPlatformProviderId).toBe(7);
    expect(state.selectedSessionPlatformKey).toBe("ps5");
  });

  it("provider mutations are mutually exclusive with platform changes", () => {
    const p = providerWithPlatforms(7);
    let state = s.selectSessionPlatform(s.beginCredentialEdit(loaded(p), p), p, "ps5")!;
    const pending = state;
    expect(() => s.beginProviderUpdate(pending, p.id)).toThrow(s.ProviderChangePendingError);
    expect(() => s.beginCredentialSave(pending, p.id)).toThrow(s.ProviderChangePendingError);

    state = s.beginSessionPlatformSave(state, p.id);
    expect(s.isProviderMutationInProgress(state, p.id)).toBe(true);
    expect(s.selectSessionPlatform(state, p, "psn")).toBeNull();

    state = s.cancelSessionPlatformChange(s.endSessionPlatformSave(state, p.id));
    state = s.beginProviderUpdate(state, p.id);
    const updating = state;
    expect(() => s.beginSessionPlatformSave(updating, p.id)).toThrow(s.ProviderChangePendingError);
  });

  it("selecting an unknown platform key is rejected and selecting the current one clears pending", () => {
    const p = providerWithPlatforms(7);
    const state = loaded(p);
    expect(s.selectSessionPlatform(state, p, "xbox")).toBeNull();
    const pending = s.selectSessionPlatform(state, p, "ps5")!;
    expect(s.selectedSessionPlatformKey(pending, p)).toBe("ps5");
    const back = s.selectSessionPlatform(pending, p, "psn")!;
    expect(back.pendingSessionPlatformProviderId).toBeNull();
    expect(s.selectedSessionPlatformKey(back, p)).toBe("psn");
  });

  it("loads retain old providers while loading and failure clears them", () => {
    const p = provider(1, "steam", "active");
    let state = s.beginProvidersLoad(loaded(p));
    expect(state.isProvidersLoading).toBe(true);
    expect(state.providers).toEqual([p]);
    state = s.failProvidersLoad(state, "Provider error.");
    expect(state.providers).toEqual([]);
    expect(state.providersError).toBe("Provider error.");
    expect(state.isProvidersLoading).toBe(false);
  });

  it("visible providers hide only exact lowercase manual and local and keep order", () => {
    const state = loaded(
      provider(1, "manual", "active"),
      provider(2, "steam", "active"),
      provider(3, "local", "active"),
      provider(4, "Manual", "paused"),
      provider(5, "psn", "paused"),
    );
    expect(s.visibleProviders(state).map((item) => item.id)).toEqual([2, 4, 5]);
  });

  it("provider updates are per id, non-optimistic and replace from the response", () => {
    const steam = provider(1, "steam", "active");
    const psn = provider(2, "psn", "paused");
    let state = loaded(steam, psn);
    const steamUpdate = s.buildProviderUpdate(steam);
    const psnUpdate = s.buildProviderUpdate(psn);
    state = s.beginProviderUpdate(s.beginProviderUpdate(state, 1), 2);
    expect(steamUpdate).toEqual({ path: "/providers/status", method: "PUT", body: { providerId: 1, status: "paused" } });
    expect(Object.keys(steamUpdate.body)).toEqual(["providerId", "status"]);
    expect(psnUpdate.body.status).toBe("active");
    expect(state.providers[0].status).toBe("active");
    expect(s.isProviderUpdating(state, 1)).toBe(true);
    expect(s.isProviderUpdating(state, 2)).toBe(true);

    const updatedSteam = { ...steam, status: "paused", accountId: "updated" };
    state = s.endProviderUpdate(s.completeProviderUpdate(state, updatedSteam), 1);
    expect(state.providers[0]).toBe(updatedSteam);
    expect(state.providers[1]).toBe(psn);
    expect(s.isProviderUpdating(state, 1)).toBe(false);
    expect(s.isProviderUpdating(state, 2)).toBe(true);

    state = s.endProviderUpdate(s.failProviderUpdate(state, "Provider failed."), 2);
    expect(state.providersError).toBe("Provider failed.");
    expect(state.providers[1]).toBe(psn);
  });

  it("credential edit builds the exact body and ignores locked api key changes", () => {
    const steam = provider(1, "steam", "active");
    const locked: ProviderStatus = {
      id: 2,
      platform: "psn",
      accountId: "account-2",
      status: "active",
      hasApiKey: true,
      apiKeyLocked: true,
    };
    let state = s.beginCredentialEdit(loaded(steam, locked), steam);
    expect(s.isEditingCredential(state, 1)).toBe(true);
    expect(state.editingAccountId).toBe("account-1");
    expect(state.editingApiKey).toBe("");
    state = s.setEditingApiKey(s.setEditingAccountId(state, "  new-account  "), "new-secret");
    const { submission } = s.buildCredentialSubmission(state);
    expect(submission).toEqual({ path: "/providers/1", method: "PUT", body: { accountId: "new-account", apiKey: "new-secret" } });
    expect(Object.keys(submission!.body)).toEqual(["accountId", "apiKey"]);

    state = s.beginCredentialEdit(s.cancelCredentialEdit(state), locked);
    state = s.setEditingApiKey(s.setEditingAccountId(state, "new-account"), "should-be-ignored");
    expect(Object.keys(s.buildCredentialSubmission(state).submission!.body)).toEqual(["accountId"]);
  });

  it("api credential edit never submits a key change", () => {
    const api: ProviderStatus = { id: 2, platform: "api", accountId: "local", status: "active", hasApiKey: true, apiKeyLocked: true };
    let state = s.beginCredentialEdit(loaded(api), api);
    expect(state.editingApiKey).toBe("");
    state = s.setEditingAccountId(s.setEditingApiKey(state, "replacement"), "renamed");
    expect(Object.keys(s.buildCredentialSubmission(state).submission!.body)).toEqual(["accountId"]);
  });

  it("an empty key field keeps the saved key: the key is never prefilled, sent only when typed", () => {
    const steam = provider(1, "steam", "active");
    let state = s.beginCredentialEdit(loaded(steam), steam);
    expect(state.editingApiKey).toBe("");
    expect(s.buildCredentialSubmission(s.setEditingApiKey(state, "   ")).submission).toBeNull();

    state = s.setEditingApiKey(state, "  new-key  ");
    expect(s.buildCredentialSubmission(state).submission!.body).toEqual({ apiKey: "new-key" });
  });

  it("keeps the issued Playdex app key until the page is left, and the error of a failed issue", () => {
    let state = s.beginAppKeyIssue(s.createProvidersState());
    expect(state.isAppKeyIssuing).toBe(true);
    state = s.completeAppKeyIssue(state, "plx_new");
    expect(state).toMatchObject({ issuedAppKey: "plx_new", isAppKeyIssuing: false, appKeyError: "" });
    state = s.completeProvidersLoad(state, { providers: [] });
    expect(state.issuedAppKey).toBe("plx_new");

    state = s.failAppKeyIssue(s.beginAppKeyIssue(state), "Server error.");
    expect(state).toMatchObject({ issuedAppKey: "plx_new", isAppKeyIssuing: false, appKeyError: "Server error." });
    expect(s.beginAppKeyIssue(state).appKeyError).toBe("");
  });

  it("credential edit rejects no changes (including whitespace-only edits) and completes by replacing the provider", () => {
    const steam = provider(1, "steam", "active");
    let state = s.beginCredentialEdit(loaded(steam), steam);
    let result = s.buildCredentialSubmission(state);
    expect(result.submission).toBeNull();
    expect(result.state.credentialError).toBe("No changes to save.");

    result = s.buildCredentialSubmission(s.setEditingAccountId(state, " account-1 "));
    expect(result.submission).toBeNull();
    expect(result.state.credentialError).toBe("No changes to save.");

    state = s.setEditingAccountId(state, "");
    expect(s.buildCredentialSubmission(state).submission!.body).toEqual({ accountId: null });

    state = s.setEditingAccountId(state, "changed");
    result = s.buildCredentialSubmission(state);
    expect(result.submission).not.toBeNull();
    expect(result.state.credentialError).toBe("");
    state = s.beginCredentialSave(result.state, 1);
    const updated = { ...steam, accountId: "changed" };
    state = s.completeCredentialSave(state, updated);
    expect(state.providers[0]).toBe(updated);
    expect(s.isEditingCredential(state, 1)).toBe(false);
    expect(s.isCredentialSaving(state, 1)).toBe(false);
    expect(state.editingAccountId).toBe("");
  });

  it.each([
    ["steam", "S"],
    ["psn", "P"],
    ["playnite", "PL"],
    ["xbox", "X"],
    ["custom", "PX"],
  ])("presents %s with the exact mark", (platform, label) => {
    expect(s.presentProvider(provider(1, platform, "active")).label).toBe(label);
  });

  it("explains an automatic pause only while the integration stays paused", () => {
    const paused = { ...provider(1, "psn", "paused"), pausedReason: "OpenXBL rejected the API key." };
    expect(s.pauseNotice(paused)).toBe(
      "Paused automatically: OpenXBL rejected the API key. Update the credential under Manage, then resume.",
    );
    expect(s.pauseNotice({ ...paused, pausedReason: "Steam rejected the key" })).toBe(
      "Paused automatically: Steam rejected the key. Update the credential under Manage, then resume.",
    );
    expect(s.pauseNotice(provider(1, "psn", "paused"))).toBeNull();
    expect(s.pauseNotice({ ...paused, pausedReason: "  " })).toBeNull();
    expect(s.pauseNotice({ ...paused, status: "active" })).toBeNull();
  });

  it("presentation preserves empty subtitles and uses null fallbacks", () => {
    const base = { id: 1, status: "active", hasApiKey: true, apiKeyLocked: false };
    expect(s.presentProvider({ ...base, platform: "steam", accountId: null }).subtitle).toBe("Local");
    expect(s.presentProvider({ ...base, platform: "steam", accountId: "" }).subtitle).toBe("");
    expect(s.presentProvider({ ...base, platform: "psn", accountId: "a" }).name).toBe("PlayStation");
  });
});
