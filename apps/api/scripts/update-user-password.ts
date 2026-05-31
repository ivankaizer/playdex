import { createDb, ensureDatabaseExists } from "../src/common/db/database";
import { runMigrations } from "../src/common/db/migrations";

const databaseUrl = Bun.env.DATABASE_URL;
if (!databaseUrl?.trim()) {
  console.error("DATABASE_URL is required.");
  process.exit(1);
}

const getArgValue = (name: string) => {
  const flag = `--${name}`;
  const prefix = `${flag}=`;
  const args = Bun.argv.slice(2);

  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (arg.startsWith(prefix)) {
      return arg.slice(prefix.length).trim();
    }
    if (arg === flag) {
      return (args[index + 1] ?? "").trim();
    }
  }

  return "";
};

const userIdRaw = getArgValue("user-id");
const passwordHash = getArgValue("password-hash");
const userId = Number(userIdRaw);

if (!userIdRaw || !Number.isFinite(userId) || userId <= 0 || !passwordHash) {
  console.error(
    "Usage: bun run scripts/update-user-password.ts --user-id 123 --password-hash <bcrypt>"
  );
  process.exit(1);
}

try {
  await ensureDatabaseExists(databaseUrl);
} catch (error) {
  console.error("Database setup failed.");
  console.error(error);
  process.exit(1);
}

const db = createDb(databaseUrl, { max: 1 });

try {
  await runMigrations({ db });

  const rows = await db
    .updateTable("users")
    .set((eb) => ({
      password: passwordHash,
      updated_at: eb.fn<Date>("now", []),
    }))
    .where("id", "=", String(userId))
    .returning("id")
    .execute();

  if (rows.length === 0) {
    console.error(`No user found for id ${userId}`);
    process.exitCode = 1;
  } else {
    console.log(`Updated password for user ${rows[0].id}`);
  }
} catch (error) {
  console.error("Failed to update user password.");
  console.error(error);
  process.exitCode = 1;
} finally {
  await db.destroy();
}
