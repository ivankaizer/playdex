import { expect, test } from "bun:test";

import { toDomainId } from "../src/common/db/ids";

test("keeps positive safe numeric identifiers unchanged", () => {
  expect(toDomainId(42, "games.id")).toBe(42);
  expect(toDomainId(Number.MAX_SAFE_INTEGER, "games.id")).toBe(
    Number.MAX_SAFE_INTEGER
  );
});

test("normalizes Postgres.js bigint strings", () => {
  expect(toDomainId("42", "games.id")).toBe(42);
});

test("rejects identifiers that are not positive safe integers", () => {
  const invalidValues = [
    0,
    "0",
    -1,
    "-1",
    "",
    "invalid",
    "12.5",
    Number.NaN,
    Number.POSITIVE_INFINITY,
    "9007199254740992",
  ];

  for (const value of invalidValues) {
    expect(() => toDomainId(value, "games.id")).toThrow(
      "games.id must be a positive safe integer."
    );
  }
});
