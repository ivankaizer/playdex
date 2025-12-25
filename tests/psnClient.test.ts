import { beforeEach, expect, mock, test } from "bun:test";
import { readFile } from "node:fs/promises";
import { join } from "node:path";

import { PsnClient } from "../src/services/psnClient";

const makeAccessToken = (accountId: string) => {
  const payload = JSON.stringify({ account_id: accountId });
  const base64 = Buffer.from(payload, "utf8").toString("base64");
  const base64Url = base64
    .replace(/=+$/g, "")
    .replace(/\+/g, "-")
    .replace(/\//g, "_");
  return `header.${base64Url}.signature`;
};

const loadFixture = async (name: string) => {
  const filePath = join(process.cwd(), "tests", "mocks", "http", name);
  const contents = await readFile(filePath, "utf8");
  return JSON.parse(contents) as Record<string, unknown>;
};

const apiMock = {
  exchangeNpssoForCode: mock(async () => "code"),
  exchangeCodeForAccessToken: mock(async () => ({
    accessToken: makeAccessToken("acct-123"),
    expiresIn: 3600,
    refreshToken: "refresh",
  })),
  getBasicPresence: mock(async () => ({})),
};

mock.module("psn-api", () => apiMock);

beforeEach(() => {
  apiMock.exchangeNpssoForCode.mockClear();
  apiMock.exchangeCodeForAccessToken.mockClear();
  apiMock.getBasicPresence.mockClear();
});

test("PsnClient resolves account id from access token", async () => {
  const client = new PsnClient({ npsso: "token" });
  const accountId = await client.resolveAccountId("unused");

  expect(accountId).toBe("acct-123");
  expect(apiMock.exchangeNpssoForCode).toHaveBeenCalledTimes(1);
  expect(apiMock.exchangeCodeForAccessToken).toHaveBeenCalledTimes(1);
});

test("PsnClient returns a game when presence includes a title", async () => {
  apiMock.getBasicPresence.mockResolvedValueOnce(
    await loadFixture("psn.playing.json")
  );
  const client = new PsnClient({ npsso: "token" });
  const result = await client.getCurrentlyPlaying({
    platform: "psn",
    accountId: "acct-123",
  });

  expect(result.game?.title).toBe("Skate Story");
  expect(result.game?.externalId).toBe("PPSA28801_00");
  expect(apiMock.getBasicPresence).toHaveBeenCalledTimes(1);
});

test("PsnClient returns null game when not playing", async () => {
  apiMock.getBasicPresence.mockResolvedValueOnce(
    await loadFixture("psn.not_playing.json")
  );
  const client = new PsnClient({ npsso: "token" });
  const result = await client.getCurrentlyPlaying({
    platform: "psn",
    accountId: "acct-123",
  });

  expect(result.game).toBeNull();
});
