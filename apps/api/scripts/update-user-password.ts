import postgres from "postgres";

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

const sql = postgres(databaseUrl, {
  max: 1,
  onnotice: (notice) => {
    if (notice.code === "42P07") {
      return;
    }
    console.warn(notice);
  },
});

try {
  await runMigrations({ sql });

  const rows = await sql<{ id: number }[]>`
    update users
    set password = ${passwordHash}, updated_at = now()
    where id = ${userId}
    returning id
  `;

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
  await sql.end();
}
