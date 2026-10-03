import { describe, expect, it } from "vitest";
import type { GameSummary, IntegrationSummary } from "./api";
import {
  aiErrorsLink,
  credentialFormFor,
  gameFormFor,
  gameUpdateFor,
  generationLink,
  hasNextPage,
  integrationUpdateFor,
  mergeValidationError,
  pageOf,
  sessionsTodayLink,
  sourceLabel,
  userInitials,
} from "./logic";

describe("userInitials", () => {
  it.each([
    ["ivan.kaizer@example.com", "IK"],
    ["admin@playdex.local", "AP"],
    ["x", "X"],
    ["", ""],
  ])("%s → %s", (email, expected) => expect(userInitials(email)).toBe(expected));
});

describe("dashboard links", () => {
  it("scope sessions to the local day and AI errors to the last 24h", () => {
    expect(sessionsTodayLink("2026-09-15T22:00:00.000Z", "2026-09-16T22:00:00.000Z")).toBe(
      "/admin/sessions?from=2026-09-15T22%3A00%3A00.000Z&to=2026-09-16T22%3A00%3A00.000Z",
    );
    const errors = new URL(aiErrorsLink("2026-09-16T12:00:00.000Z"), "https://x.test");
    expect(errors.pathname).toBe("/admin/ai-generations");
    expect(errors.searchParams.get("status")).toBe("errors");
    expect(errors.searchParams.get("since")).toBe("2026-09-15T12:00:00.000Z");
    expect(errors.searchParams.get("until")).toBe("2026-09-16T12:00:00.000Z");
    expect(generationLink("abc")).toBe("/admin/ai-generations?id=abc");
  });

  it("labels capture sources", () => {
    expect([sourceLabel("polling"), sourceLabel("api"), sourceLabel("manual")]).toEqual(["Polling", "API", "Manual"]);
  });
});

describe("paging", () => {
  it("pages locally and clamps out-of-range pages", () => {
    const items = Array.from({ length: 45 }, (_, index) => index);
    expect(pageOf(items, 0, 20).rows).toHaveLength(20);
    expect(pageOf(items, 2, 20)).toEqual({ rows: [40, 41, 42, 43, 44], page: 2, pages: 3 });
    expect(pageOf(items, 9, 20).page).toBe(2);
    expect(pageOf([], 0, 20)).toEqual({ rows: [], page: 0, pages: 1 });
  });

  it("knows when a server page has a successor", () => {
    expect(hasNextPage(0, 25, 27)).toBe(true);
    expect(hasNextPage(1, 25, 27)).toBe(false);
    expect(hasNextPage(0, 25, 25)).toBe(false);
  });
});

describe("game edits", () => {
  const game: GameSummary = { id: 7, title: "Hades", capsuleUrl: null, titleLocked: false, capsuleUrlLocked: true };

  it("marks only changed fields and always sends the lock checkboxes", () => {
    const form = gameFormFor(game);
    expect(gameUpdateFor(game, form)).toEqual({
      title: "Hades",
      titleChanged: false,
      capsuleUrl: null,
      capsuleUrlChanged: false,
      titleLocked: false,
      capsuleUrlLocked: true,
    });
    const edited = gameUpdateFor(game, { ...form, title: "Hades II", capsuleUrl: "https://img", titleLocked: true });
    expect(edited).toMatchObject({ titleChanged: true, capsuleUrlChanged: true, titleLocked: true, capsuleUrlLocked: true });
  });

  it("treats a cleared capsule input as a change that removes the image", () => {
    const withImage = { ...game, capsuleUrl: "https://img" };
    expect(gameUpdateFor(withImage, { ...gameFormFor(withImage), capsuleUrl: "" })).toMatchObject({
      capsuleUrl: "",
      capsuleUrlChanged: true,
    });
  });

  it("requires two different games to merge", () => {
    expect(mergeValidationError(null, 2)).toBe("Select two different games.");
    expect(mergeValidationError(2, 2)).toBe("Select two different games.");
    expect(mergeValidationError(1, 2)).toBeNull();
  });
});

describe("credential edits", () => {
  const integration: IntegrationSummary = {
    id: 1,
    email: "owner@example.test",
    platform: "steam",
    accountId: "steam-account",
    active: true,
    apiKey: "steam-test-key",
  };

  it("prefills current values but changes nothing until a field is selected", () => {
    const form = credentialFormFor(integration);
    expect(form).toEqual({ replaceApiKey: false, apiKey: "steam-test-key", changeAccountId: false, accountId: "steam-account" });
    expect(integrationUpdateFor(form)).toBeNull();
  });

  it("sends only the selected intent and clears the key when emptied", () => {
    const form = credentialFormFor(integration);
    expect(integrationUpdateFor({ ...form, changeAccountId: true, accountId: "  changed-account  " })).toEqual({
      apiKeyChanged: false,
      apiKey: null,
      accountIdChanged: true,
      accountId: "  changed-account  ",
    });
    expect(integrationUpdateFor({ ...form, replaceApiKey: true, apiKey: "" })).toEqual({
      apiKeyChanged: true,
      apiKey: null,
      accountIdChanged: false,
      accountId: null,
    });
  });
});
