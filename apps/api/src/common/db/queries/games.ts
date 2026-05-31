import type { DbExecutor } from "../database";
import { toDomainId } from "../ids";
import type { NormalizedGame } from "../../domain/game";

const loadPlatformId = async (db: DbExecutor, platform: string) => {
  const row =
    (await db
      .selectFrom("platforms")
      .select("id")
      .where("key", "=", platform)
      .executeTakeFirst()) ??
    (await db
      .selectFrom("platforms")
      .select("id")
      .where("key", "=", "unknown")
      .executeTakeFirstOrThrow());

  return row.id;
};

export const upsertGame = async (
  db: DbExecutor,
  game: NormalizedGame
): Promise<number> => {
  const platformId = await loadPlatformId(db, game.platform);
  const row = await db
    .insertInto("games")
    .values({
      platform_id: platformId,
      title: game.title,
      external_id: game.externalId ?? null,
      canonical_key: game.canonicalKey,
      capsule_url: game.capsuleUrl ?? null,
    })
    .onConflict((conflict) =>
      conflict.column("canonical_key").doUpdateSet((eb) => ({
        title: eb
          .case()
          .when("games.title_locked", "=", true)
          .then(eb.ref("games.title"))
          .else(eb.ref("excluded.title"))
          .end(),
        platform_id: eb.ref("excluded.platform_id"),
        external_id: eb.ref("excluded.external_id"),
        capsule_url: eb
          .case()
          .when("games.capsule_url_locked", "=", true)
          .then(eb.ref("games.capsule_url"))
          .else(eb.fn.coalesce("excluded.capsule_url", "games.capsule_url"))
          .end(),
        updated_at: eb.fn<Date>("now", []),
      }))
    )
    .returning("id")
    .executeTakeFirstOrThrow();

  return toDomainId(row.id, "games.id");
};
