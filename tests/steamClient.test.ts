import { afterEach, expect, test } from "bun:test";
import { readFile } from "node:fs/promises";
import { join } from "node:path";

import { SteamClient } from "../src/infrastructure/clients/steam-client";

const originalFetch = globalThis.fetch;

const createMockFetch = (
  handler: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
): typeof fetch =>
  Object.assign(handler, {
    preconnect:
      originalFetch.preconnect?.bind(originalFetch) ??
      ((() => undefined) as typeof originalFetch.preconnect),
  });

afterEach(() => {
  globalThis.fetch = originalFetch;
});

test("SteamClient throws when accountId is missing", async () => {
  const client = new SteamClient({ apiKey: "key" });
  await expect(
    client.getCurrentlyPlaying({ platform: "steam", accountId: "" })
  ).rejects.toThrow("Steam accountId is required");
});

test("SteamClient surfaces retry-after on 429", async () => {
  globalThis.fetch = createMockFetch(async () =>
    new Response("rate limited", {
      status: 429,
      headers: { "retry-after": "120" },
    })
  );

  const client = new SteamClient({ apiKey: "key", steamId: "123" });

  try {
    await client.getCurrentlyPlaying({ platform: "steam", accountId: "" });
    throw new Error("Expected SteamClient to throw.");
  } catch (error) {
    const err = error as Error & { status?: number; retryAfterMs?: number };
    expect(err.status).toBe(429);
    expect(err.retryAfterMs).toBe(120000);
  }
});

const loadFixture = async (name: string) => {
  const filePath = join(process.cwd(), "tests", "mocks", "http", name);
  const contents = await readFile(filePath, "utf8");
  return JSON.parse(contents) as Record<string, unknown>;
};

test("SteamClient returns game payload when playing", async () => {
  const payload = await loadFixture("steam.playing.json");
  globalThis.fetch = createMockFetch(
    async () => new Response(JSON.stringify(payload), { status: 200 })
  );

  const client = new SteamClient({
    apiKey: "key",
    steamId: "76561190000000001",
  });
  const result = await client.getCurrentlyPlaying({
    platform: "steam",
    accountId: "76561190000000001",
  });

  expect(result.game?.title).toBe("Warhammer 40,000: Rogue Trader");
  expect(result.game?.externalId).toBe("2186680");
  expect(result.accountId).toBe("76561190000000001");
});

test("SteamClient returns null game when not playing", async () => {
  const payload = await loadFixture("steam.not_playing.json");
  globalThis.fetch = createMockFetch(
    async () => new Response(JSON.stringify(payload), { status: 200 })
  );

  const client = new SteamClient({
    apiKey: "key",
    steamId: "76561190000000001",
  });
  const result = await client.getCurrentlyPlaying({
    platform: "steam",
    accountId: "76561190000000001",
  });

  expect(result.game).toBeNull();
});
