import type postgres from "postgres";

import { getSql } from "../../common/db/postgres";

type Sql = ReturnType<typeof postgres>;

type ProviderStatusRow = {
  id: number;
  platform: string;
  account_id: string | null;
  active: boolean;
  api_key: string | null;
};

const listProvidersForUser = async (userId: number, sql: Sql = getSql()) =>
  sql<ProviderStatusRow[]>`
    select pc.id, p.key as platform, pc.account_id, pc.active, pc.api_key
    from platform_credentials pc
    join platforms p on p.id = pc.platform_id
    where pc.user_id = ${userId}
    order by pc.id
  `;

const loadProviderByPlatform = async (
  userId: number,
  platform: string,
  sql: Sql = getSql()
) => {
  const rows = await sql<ProviderStatusRow[]>`
    select pc.id, p.key as platform, pc.account_id, pc.active, pc.api_key
    from platform_credentials pc
    join platforms p on p.id = pc.platform_id
    where pc.user_id = ${userId}
      and p.key = ${platform}
    limit 1
  `;
  return rows[0] ?? null;
};

const updateProviderApiKey = async (
  credentialId: number,
  apiKey: string,
  sql: Sql = getSql()
) => {
  const rows = await sql<ProviderStatusRow[]>`
    update platform_credentials pc
    set api_key = ${apiKey}, updated_at = now()
    from platforms p
    where pc.id = ${credentialId}
      and p.id = pc.platform_id
    returning pc.id, p.key as platform, pc.account_id, pc.active, pc.api_key
  `;
  return rows[0] ?? null;
};

const updateProviderActive = async (
  userId: number,
  credentialId: number,
  active: boolean,
  sql: Sql = getSql()
) => {
  const rows = await sql<ProviderStatusRow[]>`
    update platform_credentials pc
    set active = ${active}, updated_at = now()
    from platforms p
    where pc.id = ${credentialId}
      and pc.user_id = ${userId}
      and p.id = pc.platform_id
    returning pc.id, p.key as platform, pc.account_id, pc.active, pc.api_key
  `;
  return rows[0] ?? null;
};

const createPlayniteCredential = async (
  userId: number,
  apiKey: string,
  sql: Sql = getSql()
) =>
  sql<ProviderStatusRow[]>`
    insert into platform_credentials (user_id, platform_id, api_key, account_id, active)
    values (
      ${userId},
      (select id from platforms where key = 'playnite'),
      ${apiKey},
      'local',
      true
    )
    returning
      id,
      (select key from platforms where key = 'playnite') as platform,
      account_id,
      active,
      api_key
  `;

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
