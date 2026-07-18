import { expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { join } from "node:path";

test("shared HTTP types do not own module DTOs", () => {
  const contents = readFileSync(
    join(import.meta.dir, "../src/app/http/types.ts"),
    {
      encoding: "utf8",
    }
  );
  const exportedTypes = [...contents.matchAll(/export type (\w+)/g)].map(
    (match) => match[1]
  );

  expect(exportedTypes).toEqual(["ApiErrorResponse", "ApiResponse"]);
});
