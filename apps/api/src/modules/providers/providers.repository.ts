import { getDb, type DbExecutor } from "../../common/db/database";
import { toDomainId } from "../../common/db/ids";

type ProviderStatusRow = {
  id: number;
  platform: string;
  account_id: string | null;
  active: boolean;
  api_key: string | null;
};

const normalizeProviderStatusRow = (
  row: Omit<ProviderStatusRow, "id"> & { id: string }
): ProviderStatusRow => ({
  ...row,
  id: toDomainId(row.id, "platform_credentials.id"),
});

const listProvidersForUser = async (
  userId: number,
  db: DbExecutor = getDb()
) => {
  const rows = await db
    .selectFrom("platform_credentials as pc")
    .innerJoin("platforms as p", "p.id", "pc.platform_id")
    .select([
      "pc.id",
      "p.key as platform",
      "pc.account_id",
      "pc.active",
      "pc.api_key",
    ])
    .where("pc.user_id", "=", String(userId))
    .orderBy("pc.id")
    .execute();

  return rows.map(normalizeProviderStatusRow);
};

const loadProviderByPlatform = async (
  userId: number,
  platform: string,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .selectFrom("platform_credentials as pc")
    .innerJoin("platforms as p", "p.id", "pc.platform_id")
    .select([
      "pc.id",
      "p.key as platform",
      "pc.account_id",
      "pc.active",
      "pc.api_key",
    ])
    .where("pc.user_id", "=", String(userId))
    .where("p.key", "=", platform)
    .executeTakeFirst();

  return row ? normalizeProviderStatusRow(row) : null;
};

const updateProviderApiKey = async (
  credentialId: number,
  apiKey: string,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .updateTable("platform_credentials as pc")
    .set((eb) => ({ api_key: apiKey, updated_at: eb.fn<Date>("now") }))
    .from("platforms as p")
    .where("pc.id", "=", String(credentialId))
    .whereRef("p.id", "=", "pc.platform_id")
    .returning([
      "pc.id",
      "p.key as platform",
      "pc.account_id",
      "pc.active",
      "pc.api_key",
    ])
    .executeTakeFirst();

  return row ? normalizeProviderStatusRow(row) : null;
};

const updateProviderActive = async (
  userId: number,
  credentialId: number,
  active: boolean,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .updateTable("platform_credentials as pc")
    .set((eb) => ({ active, updated_at: eb.fn<Date>("now") }))
    .from("platforms as p")
    .where("pc.id", "=", String(credentialId))
    .where("pc.user_id", "=", String(userId))
    .whereRef("p.id", "=", "pc.platform_id")
    .returning([
      "pc.id",
      "p.key as platform",
      "pc.account_id",
      "pc.active",
      "pc.api_key",
    ])
    .executeTakeFirst();

  return row ? normalizeProviderStatusRow(row) : null;
};

const createPlayniteCredential = async (
  userId: number,
  apiKey: string,
  db: DbExecutor = getDb()
) => {
  const playnitePlatformId = db
    .selectFrom("platforms")
    .select("id")
    .where("key", "=", "playnite");

  const rows = await db
    .insertInto("platform_credentials")
    .values({
      user_id: userId,
      platform_id: playnitePlatformId,
      api_key: apiKey,
      account_id: "local",
      active: true,
    })
    .returning(["id", "account_id", "active", "api_key"])
    .execute();

  return rows.map((row) =>
    normalizeProviderStatusRow({ ...row, platform: "playnite" })
  );
};

export const providersRepository = {
  listProviders: (userId: number) => listProvidersForUser(userId),
  loadProviderByPlatform: (userId: number, platform: string) =>
    loadProviderByPlatform(userId, platform),
  updateProviderActive: (userId: number, providerId: number, active: boolean) =>
    updateProviderActive(userId, providerId, active),
  updateProviderApiKey: (providerId: number, apiKey: string) =>
    updateProviderApiKey(providerId, apiKey),
  createPlayniteCredential: (userId: number, apiKey: string) =>
    createPlayniteCredential(userId, apiKey),
};
