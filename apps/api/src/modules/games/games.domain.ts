import type { NormalizedGame } from "../../common/domain/game";
import type { PlatformGame } from "../../common/domain/platform";
export type { NormalizedGame } from "../../common/domain/game";

const normalizeTitle = (title: string) =>
  title
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();

export const normalizeGameTitle = (title: string) => normalizeTitle(title);

const buildGameKey = (
  platform: string,
  externalId: string | undefined,
  canonicalTitle: string
) => `${platform}:${externalId ?? canonicalTitle}`;

export const normalizeGame = (game: PlatformGame): NormalizedGame => {
  const canonicalTitle = normalizeTitle(game.title);
  const capsuleUrl =
    game.capsuleUrl ??
    (game.platform === "steam" && game.externalId
      ? `https://shared.steamstatic.com/store_item_assets/steam/apps/${game.externalId}/library_600x900_2x.jpg`
      : undefined);

  return {
    platform: game.platform,
    title: game.title,
    externalId: game.externalId,
    capsuleUrl,
    canonicalTitle,
    canonicalKey: buildGameKey(game.platform, game.externalId, canonicalTitle),
  };
};
