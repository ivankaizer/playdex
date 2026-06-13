import { expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { join } from "node:path";

import { createApp } from "../src/app/createApp";

const handlerFiles = [
  "accounts/accounts.handlers.ts",
  "admin/admin.handlers.ts",
  "auth/auth.handlers.ts",
  "games/games.handlers.ts",
  "manual/manual.handlers.ts",
  "metrics/metrics.handlers.ts",
  "providers/providers.handlers.ts",
  "recap/recap.handlers.ts",
  "settings/settings.handlers.ts",
];

test("protected route handlers use auth context instead of calling requireAuth", () => {
  const offenders = handlerFiles.filter((file) => {
    const contents = readFileSync(
      join(import.meta.dir, "../src/modules", file),
      {
        encoding: "utf8",
      }
    );
    return contents.includes("requireAuth");
  });

  expect(offenders).toEqual([]);
});

test("admin route handlers do not perform admin authorization inline", () => {
  const contents = readFileSync(
    join(import.meta.dir, "../src/modules/admin/admin.handlers.ts"),
    {
      encoding: "utf8",
    }
  );

  expect(contents).not.toContain("requireAdmin");
  expect(contents).not.toContain("Admin access required");
});

test("bearer auth middleware applies to protected routes without covering Playnite api-key routes", async () => {
  Bun.env.JWT_SECRET = "auth-middleware-test-secret";
  const app = createApp();

  const protectedResponse = await app.handle(
    new Request("http://localhost/api/sessions")
  );
  expect(protectedResponse.status).toBe(401);
  expect(await protectedResponse.json()).toEqual({
    data: null,
    error: { error: "Missing token." },
  });

  const playniteResponse = await app.handle(
    new Request("http://localhost/api/playnite/sessions", {
      method: "POST",
      body: JSON.stringify({}),
      headers: { "content-type": "application/json" },
    })
  );
  expect(playniteResponse.status).toBe(401);
  expect(await playniteResponse.json()).toEqual({
    data: null,
    error: { error: "Missing api key." },
  });
});
