# Repository Guidelines

## Project Structure & Module Organization
- `src/` holds the application source. The entry point is `src/index.ts`.
- There is no dedicated `tests/` or `assets/` directory yet.
- Configuration lives at the repository root (`package.json`, `tsconfig.json`, `bun.lock`).

## Build, Test, and Development Commands
- `bun run dev` starts the Elysia server with file watching and reloads on changes.
- `bun test` currently exits with an error; add a real test runner before relying on it.
- Example: run `bun run dev` and open `http://localhost:3000/` to verify the server responds.

## Coding Style & Naming Conventions
- Language: TypeScript with Bun runtime and Elysia.
- Indentation: 2 spaces; keep lines short and readable.
- Strings: prefer double quotes to match current files.
- Naming: keep file names lowercase (`index.ts`) and use clear route handler names.
- No formatter or linter is configured; if you add one, document it here and in `package.json`.

## Testing Guidelines
- There is no test framework configured yet.
- When tests are added, keep them in `tests/` or alongside modules as `*.test.ts`.
- Update `bun test` to run the chosen framework (e.g., `bun test`, `vitest`, or `jest`).

## Commit & Pull Request Guidelines
- Current history shows a single commit: “Initial commit (via bun create)”, so no convention is established.
- Use concise, imperative commit messages (e.g., “Add health check route”).
- PRs should include: a short description of changes, steps to verify, and screenshots/logs if behavior changes.

## Configuration Tips
- The server binds to port `3000` by default; update `src/index.ts` if you need a different port.
- Keep environment-specific settings out of source; use `.env` files if you add configuration in the future.
