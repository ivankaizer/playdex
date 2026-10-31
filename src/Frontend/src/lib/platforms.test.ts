import { describe, expect, it } from "vitest";
import { platformColor } from "./platforms";

describe("platformColor", () => {
  it("gives the stores and consoles Playdex records their own colors", () => {
    expect(platformColor("steam", "Steam")).toBe("#e07a0f");
    expect(platformColor("gog", "GOG")).toBe("#a3369f");
    expect(platformColor("epic", "Epic Games")).toBe("#64748b");
    expect(platformColor("xbox-series-xs", "Xbox Series X|S")).toBe("#2f9e44");
  });

  it("colors every PlayStation the same, by its key or its name", () => {
    for (const key of ["ps5", "ps4", "ps3", "psn"]) expect(platformColor(key, key.toUpperCase())).toBe("#7c3aed");
    expect(platformColor(null, "PlayStation 5")).toBe("#7c3aed");
  });
});
