import { describe, expect, it } from "vitest";
import { decodeGameHash, encodeGameId } from "./gameHash";
import { normalizeGameHash } from "./navigation";

describe("game hashes", () => {
  it("keeps an all-digit hash instead of re-encoding its digits as a raw id", () => {
    const hash = encodeGameId(87421);
    expect(hash).toMatch(/^[0-9]+$/);
    expect(decodeGameHash(hash)).toBe(87421);
    expect(encodeGameId(hash)).toBe(hash);
    expect(normalizeGameHash(hash)).toBe(hash);
  });

  it("still encodes raw numeric ids that are not hashes", () => {
    expect(decodeGameHash("42")).toBeNull();
    expect(encodeGameId("42")).toBe(encodeGameId(42));
    expect(normalizeGameHash("42")).toBe(encodeGameId(42));
  });

  it("round-trips ids", () => {
    for (let id = 1; id <= 5000; id++) expect(decodeGameHash(encodeGameId(id))).toBe(id);
    expect(encodeGameId("not-a-hash")).toBe("");
    expect(encodeGameId(0)).toBe("");
  });
});
