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

const email = getArgValue("email");
const passwordHash = getArgValue("password-hash");

if (!email || !passwordHash) {
  console.error(
    "Usage: bun run scripts/create-user.ts --email you@example.com --password-hash <bcrypt>"
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

  const row = await db
    .insertInto("users")
    .values({ email, password: passwordHash })
    .returning("id")
    .executeTakeFirst();

  console.log(`Created user ${row?.id ?? "?"} for ${email}`);
} catch (error) {
  console.error("Failed to create user.");
  console.error(error);
  process.exitCode = 1;
} finally {
  await db.destroy();
}
