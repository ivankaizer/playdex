import { config } from "./config/env";
import { loadPollingAccounts } from "./application/polling-accounts";
import { startPolling } from "./application/polling";
import { getSql } from "./infrastructure/db/postgres";
import { runMigrations } from "./infrastructure/db/migrations";
import { createServer } from "./presentation/server";

const startServer = async () => {
  await runMigrations();

  const app = createServer().listen(3000);

  console.log(
    `🦊 Elysia is running at ${app.server?.hostname}:${app.server?.port}`
  );
  console.log(`Polling interval: ${config.pollIntervalSeconds}s`);

  await startPolling({
    sql: getSql(),
    loadAccounts: loadPollingAccounts,
    pollIntervalSeconds: config.pollIntervalSeconds,
  });
};

startServer().catch((error) => {
  console.error("Startup failed.");
  console.error(error);
});
