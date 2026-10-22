# Contributing

Thanks for helping. Bug reports, fixes and small improvements are welcome as issues and pull requests. For a bigger
change, open an issue first so we can agree on the approach before you spend time on it.

## Setup

You need the .NET SDK from `global.json` (10.0.3xx), [Bun](https://bun.sh) 1.3.14 and Docker.

```sh
docker compose up -d db wiremock apprise   # Postgres, fake Steam/PSN/Xbox APIs, notifications

export DATABASE_URL=postgresql://playdex:playdex@localhost:5432/playdex
export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:3000
dotnet run --project src/Api               # applies the migrations, then serves http://localhost:3000/api
dotnet run --project src/Api -- create-user you@example.com --admin   # prints a password

cd src/Frontend && bun install
VITE_API_BASE_URL=http://localhost:3000/api/ bun run dev   # http://localhost:5173
```

In Development the API polls the Wiremock fakes in `tools/Wiremock` instead of the real platforms.
`docker compose up -d` runs the API and web app in containers too; without OrbStack, put
`VITE_API_BASE_URL=http://localhost:3000/api/` in `.env` first. Demo data comes from `tools/Seeder`; run it against
a **local** database only, it truncates every table.

The Windows app (`src/Desktop`, `src/Launcher`, `src/Playdex.*`) builds on Windows only; see
[src/Desktop/README.md](src/Desktop/README.md).

## Tests

```sh
dotnet build Playdex.sln -c Release --warnaserror

# integration tests: a separate, migrated database
docker compose exec db createdb -U playdex playdex_test
export PLAYDEX_INTEGRATION_DATABASE_URL=postgresql://playdex:playdex@localhost:5432/playdex_test
DATABASE_URL=$PLAYDEX_INTEGRATION_DATABASE_URL Auth__JwtSecret=local-test-secret \
  dotnet run --project src/Api -c Release -- migrate
dotnet test test/Api.Tests -c Release

cd src/Frontend && bun run typecheck && bun run test
```

Without `PLAYDEX_INTEGRATION_DATABASE_URL` the integration tests don't run. CI runs all of the above on every pull
request.

## Formatting

CI checks it, so set it up once:

```sh
bun install       # at the root: Biome for the TypeScript projects
lefthook install  # formats staged files on commit, and runs gitleaks if you have it
```

`bun run format` fixes TypeScript, JSON and CSS; `dotnet format Playdex.sln` fixes C#. The C# style rules in
`.editorconfig` are warnings, and the Release build treats warnings as errors.

## Conventions

- One feature per module under `src/Api/Modules/<Feature>`: endpoints, service, repository, DTOs and a
  `<Feature>Module` that registers them. Modules talk to each other through interfaces (for example `IGames`), not
  through each other's repositories.
- Schema changes are new classes in `src/Persistence/Migrations` added to the `MigrationCatalog` list in
  `src/Api/Program.cs`; never edit a migration that has shipped.
- Words matter: see [CONTEXT.md](CONTEXT.md) for the domain terms.
- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org): `feat(api): …`,
  `fix(launcher): …`, `docs: …`.
- Don't commit secrets. `.env` is ignored; `.env.example` lists every setting with an empty value.

## Pull requests

Keep one change per pull request, add or update tests, and describe how you checked it. UI changes need a
screenshot. By contributing you agree that your contribution is licensed under the [AGPL-3.0](LICENSE).
