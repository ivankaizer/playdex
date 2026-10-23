# Playdex — notes for coding agents

Playdex records play sessions from Steam, PlayStation, Xbox, a Windows app and manual/AI logging into one history.
Read README.md for the product, CONTRIBUTING.md for setup, CONTEXT.md for domain terms (Gaming Provider,
Notification Channel, Transport, Destination) and PRODUCT.md before UI work.

## Map

- `src/Api` — ASP.NET Core minimal API. `Program.cs` wires everything; `Infrastructure/` holds auth, HTTP
  middleware and shared types; `Modules/<Feature>/` holds one feature each: `<Feature>Endpoints`, `<Feature>Service`,
  `<Feature>Repository`, DTOs and a `<Feature>Module` with `Add<Feature>()`/`Map<Feature>()`.
- `src/Persistence` — Postgres entities, `PlaydexDbContext`, and hand-written migrations (`Migrations/*Migration.cs`,
  listed in the `MigrationCatalog` built in `src/Api/Program.cs`). Migrations run on API start and with `-- migrate`.
- `src/Frontend` — React 19 + Vite + Tailwind web app and admin console (`/admin`); `landing/` is playdex.dev.
- `src/Launcher/ui` — React UI shared by the Windows app (desktop mode) and the full-screen launcher.
- `src/Playdex.Core` (shared Windows logic, updates), `src/Playdex.Run` (per-game runner, Native AOT),
  `src/Playdex.Setup` (installer) — `net10.0-windows`, in `Playdex.sln`; they compile anywhere, but their tests
  (`test/Playdex.Core.Tests`) only run on Windows.
- `src/Desktop`, `src/Launcher/Host`, `src/Launcher/Shell` — WinUI/WebView2 apps. Not in `Playdex.sln`; they build
  on Windows only, in CI's build-windows workflow.
- `src/Docs` — docs.playdex.dev (Astro Starlight). `selfhost/` — the self-hosting compose file and Caddyfile.
- `tools/Seeder` (demo data, truncates the database it points at), `tools/Wiremock` (fake Steam/PSN/Xbox APIs).
- `test/Api.Tests` (xUnit; integration tests need `PLAYDEX_INTEGRATION_DATABASE_URL`), `test/Playdex.Core.Tests`
  (Windows only).

## Commands

```sh
dotnet build Playdex.sln -c Release --warnaserror        # must stay warning-free
dotnet test test/Api.Tests -c Release                     # see CONTRIBUTING.md for the test database
cd src/Frontend && bun run typecheck && bun run test && bun run build
cd src/Launcher/ui && bun run typecheck
bun run format && dotnet format Playdex.sln              # formatting; CI fails without it
```

## Rules

- Modules don't reach into each other's repositories or internals; they use the interfaces a module exposes
  (`IGames`, `IGameResolver`, …).
- Never edit a migration that has shipped. Add a new `*Migration` class, register it, and add a migration test.
- Every query on user data is scoped to the authenticated user id. Keep it that way.
- New settings go into `.env.example`, `selfhost/.env.example`, `src/Docs/src/content/docs/reference/configuration.mdx`
  and, if they're required, into startup validation.
- No secrets, tokens, real account ids or personal data in code, tests, fixtures or docs. The Wiremock fixtures use
  made-up ids.
- Match the surrounding code: file-scoped namespaces, primary constructors, `_camelCase` private fields, comments
  only where the code can't say it.
- Commits use Conventional Commits (`feat(api): …`, `fix(launcher): …`, `ci: …`).
- Windows code can't be compiled here: say so instead of claiming it works.
