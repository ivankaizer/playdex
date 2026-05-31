import { readdir } from "node:fs/promises";
import { existsSync } from "node:fs";
import { join, resolve } from "node:path";

const rootDir = resolve(import.meta.dir, "..");
const srcDir = join(rootDir, "src");
const migrationsDir = join(rootDir, "migrations");
const scriptsDir = join(rootDir, "scripts");

const BOUNDARY_MODULE = "src/common/db/database.ts";
const GUARD_SCRIPT = "scripts/check-db-boundary.ts";

const errors: string[] = [];

const fileExists = (path: string) => existsSync(path);

// 1. No .sql files under migrations/
if (fileExists(migrationsDir)) {
  try {
    const entries = await readdir(migrationsDir, { recursive: true });
    const sqlFiles = entries.filter((entry) => entry.endsWith(".sql"));
    if (sqlFiles.length > 0) {
      errors.push(
        `SQL migration files found under migrations/ (should be TypeScript modules): ${sqlFiles.join(", ")}`
      );
    }
  } catch {
    // migrations directory may not exist — acceptable.
  }
}

// 2. No migration files with invalid naming convention
if (fileExists(migrationsDir)) {
  try {
    const entries = await readdir(migrationsDir);
    const tsMigrations = entries.filter(
      (entry) => entry.endsWith(".ts") && entry !== "index.ts"
    );
    const validPattern = /^\d{3}_[a-z0-9_]+\.ts$/;
    for (const file of tsMigrations) {
      if (!validPattern.test(file)) {
        errors.push(
          `Migration file does not follow NNN_slug.ts convention: migrations/${file}`
        );
      }
    }
  } catch {
    // migrations directory may not exist — acceptable.
  }
}

// 3. No direct postgres imports outside the approved boundary
if (fileExists(srcDir)) {
  try {
    const { readdirSync, statSync, readFileSync } = await import("node:fs");

    const walk = (dir: string, files: string[] = []): string[] => {
      const entries = readdirSync(dir);
      for (const entry of entries) {
        const fullPath = join(dir, entry);
        const stat = statSync(fullPath);
        if (stat.isDirectory()) {
          walk(fullPath, files);
        } else if (entry.endsWith(".ts") || entry.endsWith(".tsx")) {
          files.push(fullPath);
        }
      }
      return files;
    };

    const sourceFiles = walk(srcDir);
    const scriptFiles = fileExists(scriptsDir)
      ? walk(scriptsDir).filter(
          (f) => !f.replaceAll("\\", "/").endsWith(GUARD_SCRIPT)
        )
      : [];
    const allFiles = [...sourceFiles, ...scriptFiles].filter(
      (f) => !f.replaceAll("\\", "/").endsWith(GUARD_SCRIPT)
    );

    for (const file of allFiles) {
      const relativePath = file
        .replace(rootDir, "")
        .replace(/^[\\/]/, "")
        .replaceAll("\\", "/");
      const content = readFileSync(file, "utf-8");

      // Check for direct postgres import
      if (/import\s+postgres\s+from\s+["']postgres["']/.test(content)) {
        if (relativePath !== BOUNDARY_MODULE) {
          errors.push(
            `Direct postgres import found outside the approved boundary: ${relativePath}`
          );
        }
      }

      // Check for import from the old postgres.ts bridge
      if (
        content.includes('from "./common/db/postgres"') ||
        content.includes("from './common/db/postgres'") ||
        content.includes('from "../../common/db/postgres"') ||
        content.includes("from '../../common/db/postgres'") ||
        content.includes('from "../../../common/db/postgres"') ||
        content.includes("from '../../../common/db/postgres'")
      ) {
        errors.push(
          `Import from the removed postgres.ts compatibility bridge: ${relativePath}`
        );
      }

      // Check for unsafe execution outside the boundary
      if (relativePath !== BOUNDARY_MODULE) {
        if (
          content.includes("sql.unsafe(") ||
          content.includes("sql.unsafe (")
        ) {
          errors.push(
            `Unsafe SQL execution found outside the approved boundary: ${relativePath}`
          );
        }
      }
    }
  } catch (error) {
    errors.push(
      `Failed to scan source files: ${error instanceof Error ? error.message : String(error)}`
    );
  }
}

if (errors.length > 0) {
  console.error("Database-boundary guard failed:");
  for (const error of errors) {
    console.error(`  - ${error}`);
  }
  process.exit(1);
}

console.log("Database-boundary guard passed.");
