import { config } from "./config/env";
import { loadPollingAccounts } from "./application/polling-accounts";
import { startPolling } from "./application/polling";
import {
  createTelegramNotifier,
  startTelegramBot,
} from "./application/telegram";
import { getSql } from "./infrastructure/db/postgres";
import { runMigrations } from "./infrastructure/db/migrations";
import { createServer } from "./presentation/server";

const startServer = async () => {
  await runMigrations();

  const sql = getSql();
  const telegramToken = config.telegramBotToken;
  const notifier = telegramToken
    ? createTelegramNotifier({ token: telegramToken, sql })
    : undefined;

  const app = createServer({ notifier }).listen(3000);

  console.log(
    `🦊 Elysia is running at ${app.server?.hostname}:${app.server?.port}`
  );
  console.log(`Polling interval: ${config.pollIntervalSeconds}s`);

  if (telegramToken) {
    startTelegramBot({ token: telegramToken, sql });
  } else {
    console.log("Telegram bot token not set; notifications disabled.");
  }

  await startPolling({
    sql,
    loadAccounts: loadPollingAccounts,
    pollIntervalSeconds: config.pollIntervalSeconds,
    notifier,
  });
};

startServer().catch((error) => {
  console.error("Startup failed.");
  console.error(error);
});
