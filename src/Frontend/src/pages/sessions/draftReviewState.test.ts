import { localDateOnly } from "@/lib/dates";
import type { DraftRowDto } from "@/models/drafts";
import {
  acceptance,
  applyDraftRow,
  applySavedDraftRow,
  canAccept,
  corrections,
  createDraftRowEdit,
  draftDuration,
  durationCheck,
  hasValidDate,
  isReady,
  isDirty,
  isFutureDate,
  isValidDate,
  markAccepted,
  needsPlatformReview,
  rowUpdate,
  setDraftTitle,
} from "./draftReviewState";

const row = (overrides: Partial<DraftRowDto> = {}): DraftRowDto => ({
  id: crypto.randomUUID(),
  revision: 1,
  status: "pending",
  title: "Hades",
  date: "2026-09-08",
  durationMinutes: 60,
  platformKey: "unknown",
  selection: { source: "local", gameId: "game", igdbId: null, title: "Hades", platform: null, platformId: null, capsuleUrl: null },
  warnings: ["Confirm Unknown platform."],
  ready: true,
  acceptedSessionId: null,
  ...overrides,
});

describe("draft row edit", () => {
  it("missing fields prevent acceptance and Unknown platform is allowed", () => {
    let edit = createDraftRowEdit(row());
    expect(canAccept(edit)).toBe(true);
    expect(needsPlatformReview(edit)).toBe(true);

    edit = { ...edit, date: "" };
    expect(canAccept(edit)).toBe(false);
    expect(corrections(edit).toLowerCase()).toContain("date");

    edit = { ...edit, date: "2026-09-08", minutes: "0", hours: "0" };
    expect(canAccept(edit)).toBe(false);
  });

  it("title change requires reselection and keeps the revision", () => {
    let edit = createDraftRowEdit(row());
    edit = { ...setDraftTitle(edit, "Hades II"), error: "Revision conflict. Reload to review server changes." };
    expect(edit.selection).toBeNull();
    expect(canAccept(edit)).toBe(false);
    expect(edit.title).toBe("Hades II");
    expect(edit.revision).toBe(1);
    expect(setDraftTitle(edit, "Hades II")).toBe(edit);
  });

  it("acceptance uses the saved revision and accepted rows are read-only", () => {
    const dto = row();
    let edit = applyDraftRow(createDraftRowEdit(dto), { ...dto, revision: 2 });
    expect(acceptance(edit).revision).toBe(2);
    edit = markAccepted(edit, 42);
    expect(edit.isAccepted).toBe(true);
    expect(canAccept(edit)).toBe(false);
    expect(edit.acceptedSessionId).toBe(42);
  });

  it("fractional and overflowing durations remain unready", () => {
    let edit = { ...createDraftRowEdit(row()), hours: "2147483647" };
    expect(canAccept(edit)).toBe(false);
    edit = { ...edit, hours: "0", minutes: "1.5" };
    expect(canAccept(edit)).toBe(false);
  });

  it("rejects zero, minutes over 59 and drafts over 24 hours", () => {
    const edit = createDraftRowEdit(row());
    expect(durationCheck({ hours: "0", minutes: "0" }).note).toBe("Add how long you played.");
    expect(draftDuration({ hours: "0", minutes: "75" })).toBeNull();
    expect(corrections({ ...edit, hours: "0", minutes: "75" })).toContain("Minutes must be between 0 and 59.");

    const long = { ...edit, hours: "26", minutes: "0" };
    expect(durationCheck(long)).toEqual({ minutes: null, note: "A session can be at most 24 hours." });
    expect(canAccept(long)).toBe(false);
    expect(isReady(long)).toBe(false);
    expect(isReady({ ...edit, hours: "24", minutes: "0" })).toBe(true);
  });

  it("holds back a draft dated in the future", () => {
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    const edit = { ...createDraftRowEdit(row()), date: localDateOnly(tomorrow) };
    expect(isFutureDate(edit.date)).toBe(true);
    expect(hasValidDate(edit)).toBe(false);
    expect(corrections(edit)).toContain("The date cannot be in the future.");
    expect(canAccept(edit)).toBe(false);
    expect(hasValidDate({ ...edit, date: localDateOnly(new Date()) })).toBe(true);
  });

  it("splits the server duration into hours and minutes", () => {
    expect(createDraftRowEdit(row({ durationMinutes: 125 }))).toMatchObject({ hours: "2", minutes: "5" });
    expect(createDraftRowEdit(row({ durationMinutes: null }))).toMatchObject({ hours: "0", minutes: "" });
  });

  it("tracks dirtiness against the last applied server row", () => {
    const edit = createDraftRowEdit(row());
    expect(isDirty(edit)).toBe(false);
    const changed = { ...edit, hours: "3" };
    expect(isDirty(changed)).toBe(true);
    expect(rowUpdate(changed)).toEqual({
      revision: 1,
      title: "Hades",
      date: "2026-09-08",
      durationMinutes: 180,
      platformKey: "unknown",
      gameId: "game",
      igdbId: null,
    });
    expect(rowUpdate({ ...edit, date: "  " }).date).toBeNull();
  });

  it("a save response keeps edits made while the request was in flight", () => {
    const edit = createDraftRowEdit(row());
    const sent = rowUpdate({ ...edit, hours: "2" });
    const saved = applySavedDraftRow({ ...edit, hours: "2" }, sent, row({ revision: 2, durationMinutes: 120 }));
    expect(saved.revision).toBe(2);
    expect(isDirty(saved)).toBe(false);

    const edited = applySavedDraftRow({ ...edit, hours: "2", minutes: "30" }, sent, row({ revision: 2, durationMinutes: 120 }));
    expect(edited.revision).toBe(2);
    expect(edited.minutes).toBe("30");
    expect(isDirty(edited)).toBe(true);
    expect(rowUpdate(edited)).toMatchObject({ revision: 2, durationMinutes: 150 });
  });

  it("validates dates like DateOnly.TryParseExact", () => {
    expect(isValidDate("2028-02-29")).toBe(true);
    expect(isValidDate("2026-02-29")).toBe(false);
    expect(isValidDate("2026-9-8")).toBe(false);
    expect(isValidDate("0000-01-01")).toBe(false);
  });
});
