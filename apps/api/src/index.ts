import { createApp } from "./app/createApp";
import { config } from "./common/config";
import { getDb } from "./common/db/database";
import { runMigrations } from "./common/db/migrations";
import { loadPollingAccounts, startPolling } from "./modules/polling";
import { createTelegramNotifier, startTelegramBot } from "./modules/telegram";

const startServer = async () => {
  await runMigrations();

  const db = getDb();
  const telegramToken = config.telegramBotToken;
  const notifier = telegramToken
    ? createTelegramNotifier({ token: telegramToken, db })
    : undefined;

  const app = createApp({ notifier }).listen(3000);

  console.log(
    `🦊 Elysia is running at ${app.server?.hostname}:${app.server?.port}`
  );
  console.log(`Polling interval: ${config.pollIntervalSeconds}s`);

  if (telegramToken) {
    startTelegramBot({ token: telegramToken, db });
  } else {
    console.log("Telegram bot token not set; notifications disabled.");
  }

  await startPolling({
    db,
    loadAccounts: loadPollingAccounts,
    pollIntervalSeconds: config.pollIntervalSeconds,
    notifier,
  });
};

startServer().catch((error) => {
  console.error("Startup failed.");
  console.error(error);
});
