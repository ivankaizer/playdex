import type { Platform, PlatformGame } from "./platform";

export type NormalizedGame = {
  platform: Platform;
  title: string;
  externalId?: string;
  canonicalTitle: string;
  canonicalKey: string;
};

const normalizeTitle = (title: string) =>
  title
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();

const buildGameKey = (
  platform: Platform,
  externalId: string | undefined,
  canonicalTitle: string
) => `${platform}:${externalId ?? canonicalTitle}`;

export const normalizeGame = (game: PlatformGame): NormalizedGame => {
  const canonicalTitle = normalizeTitle(game.title);

  return {
    platform: game.platform,
    title: game.title,
    externalId: game.externalId,
    canonicalTitle,
    canonicalKey: buildGameKey(game.platform, game.externalId, canonicalTitle),
  };
};
