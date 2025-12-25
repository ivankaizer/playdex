import {
  healthCheck,
  listAccountSessions,
  listAccounts,
} from "../infrastructure/db/queries";

export const checkDatabaseHealth = async () => {
  await healthCheck();
};

export const getAccountSummaries = async () => {
  const rows = await listAccounts();
  return rows.map((row) => ({ id: +row.id, name: row.platform }));
};

export const getAccountSessions = async (accountId: number) => {
  const rows = await listAccountSessions(accountId);
  return rows.map((row) => ({
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
  }));
};
