const DEFAULT_POLL_INTERVAL_SECONDS = 60;
const MIN_POLL_INTERVAL_SECONDS = 10;
const MAX_POLL_INTERVAL_SECONDS = 3600;

const requiredEnv = ["DATABASE_URL"] as const;

const missingEnv = requiredEnv.filter((key) => !Bun.env[key]?.trim());
if (missingEnv.length > 0) {
  console.error(
    `Missing required environment variables: ${missingEnv.join(", ")}`
  );
  process.exit(1);
}

const resolvePollIntervalSeconds = (raw: string | undefined) => {
  if (!raw) {
    return DEFAULT_POLL_INTERVAL_SECONDS;
  }

  const parsed = Number.parseInt(raw, 10);
  if (Number.isNaN(parsed)) {
    throw new Error("POLL_INTERVAL_SECONDS must be a number.");
  }

  const clamped = Math.min(
    Math.max(parsed, MIN_POLL_INTERVAL_SECONDS),
    MAX_POLL_INTERVAL_SECONDS
  );

  if (clamped !== parsed) {
    console.warn(
      `POLL_INTERVAL_SECONDS was clamped to ${clamped} (min ${MIN_POLL_INTERVAL_SECONDS}, max ${MAX_POLL_INTERVAL_SECONDS}).`
    );
  }

  return clamped;
};

const resolveCorsOrigins = (raw: string | undefined) => {
  if (!raw) {
    return [];
  }

  const origins = raw
    .split(",")
    .map((origin) => origin.trim())
    .filter(Boolean);

  if (origins.includes("*")) {
    return ["*"];
  }

  return origins;
};

export const config = {
  databaseUrl: Bun.env.DATABASE_URL ?? "",
  pollIntervalSeconds: resolvePollIntervalSeconds(
    Bun.env.POLL_INTERVAL_SECONDS
  ),
  accessTokenTtl: Bun.env.ACCESS_TOKEN_TTL?.trim() || "15m",
  telegramBotToken: Bun.env.TELEGRAM_BOT_TOKEN?.trim() ?? "",
  corsOrigins: resolveCorsOrigins(Bun.env.CORS_ORIGINS),
  igdbClientId: Bun.env.IGDB_CLIENT_ID?.trim() ?? "",
  igdbClientSecret: Bun.env.IGDB_CLIENT_SECRET?.trim() ?? "",
};
