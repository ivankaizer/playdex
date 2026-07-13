# React to Blazor Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the React/Vite frontend with a standalone .NET 10 Blazor WebAssembly SPA while preserving its routes, behavior, responsive layout, and light/dark appearance with one-to-one visual parity.

**Architecture:** Keep `apps/web` as a separately deployed browser application and continue serving static output through nginx. Translate the current DOM and Tailwind class strings literally into Razor, isolate browser storage and raw .NET API behavior behind small services, and keep page state local as it is in React.

**Tech Stack:** .NET 10, Blazor WebAssembly, Razor components, xUnit, Tailwind CSS 3.4, Bun for CSS compilation only, nginx, Docker Compose.

---

## Working Rules

- Work on `codex/blazor-migration`.
- The branch intentionally contains unrelated staged and unstaged .NET migration work. Never use `git add .` or a plain `git commit`; stage explicit paths and use `git commit --only ... -- <paths>` so the pre-existing index cannot leak into a migration commit.
- At the start of each task, run `git status --short -- <task paths>`. If a listed file was already dirty when this branch was created, do not include it in a migration commit unless its pre-existing hunks can be isolated safely. Leave overlapping changes uncommitted and report them rather than rewriting or committing user work.
- Use `@superpowers:test-driven-development` for implementation work and `@superpowers:verification-before-completion` before any completion claim.
- Use `@dotnet-patterns` for C# changes, `@dotnet-testing-patterns` for tests, and the endpoint/service/repository-specific .NET skills for any backend parity fix.
- Do not delete the React implementation until the Blazor equivalent builds and the React visual baseline has been preserved.
- A route is not complete until its light/dark and mobile/desktop state is compared with React.

### Task 1: Preserve the React reference contract

**Files:**

- Create: `docs/visual-parity/react-baseline.md`
- Create: `docs/visual-parity/react/home-light-mobile.png`
- Create: `docs/visual-parity/react/home-dark-mobile.png`
- Create: `docs/visual-parity/react/home-light-desktop.png`
- Create: `docs/visual-parity/react/home-dark-desktop.png`
- Reference: `apps/web/src/**/*.tsx`
- Reference: `apps/web/src/index.css`

**Step 1: Verify the current React build before changing it**

Run: `bun run build`

Working directory: `apps/web`

Expected: Vite completes successfully and writes `dist/`.

**Step 2: Record the parity matrix**

Create `docs/visual-parity/react-baseline.md` with a row for each route (`/`, `/dashboard`, `/sessions`, `/recap`, `/mapping`, `/settings`, `/admin`, `/games/{hash}`), three viewports (390×844, 768×1024, 1440×900), both themes, and these states where applicable: loading, populated, empty, error, modal open, unauthenticated, authenticated non-admin, authenticated admin, and URL query filters.

Document the immutable browser contract:

```text
playdex.token
playdex.refresh
playdex.email
playdex.isAdmin
playdex.theme
```

Also record the 300 ms Sessions title debounce, UTC week/date behavior, recap `from`/`to` query rules, protected-route redirects, admin redirect, numeric game-id acceptance, and base-62 game hashes.

**Step 3: Capture the public baseline**

Run the React app with `bun run dev -- --host 127.0.0.1`, then use the browser skill to capture the four Home screenshots named above. Record the exact browser version and viewport dimensions in the matrix.

Expected: screenshots are readable, use Space Grotesk, and show the reference responsive layout in both themes.

**Step 4: Record protected-state capture requirements**

Where a working authenticated backend fixture is unavailable, mark the protected screenshot rows `pending-auth-fixture`; do not invent screenshots or mark them passing. Retain the React sources until those rows can be captured or compared side by side against the same live API state.

**Step 5: Commit the reference artifact**

```bash
git add docs/visual-parity/react-baseline.md docs/visual-parity/react
git commit --only -m "test: preserve React visual baseline" -- docs/visual-parity/react-baseline.md docs/visual-parity/react
```

### Task 2: Scaffold the minimal Blazor host and test project

**Files:**

- Create: `apps/web/Playdex.Web.csproj`
- Create: `apps/web/Program.cs`
- Create: `apps/web/_Imports.razor`
- Create: `apps/web/App.razor`
- Create: `apps/web/Pages/Home.razor`
- Create: `apps/web/wwwroot/index.html`
- Create: `apps/web/wwwroot/appsettings.json`
- Create: `apps/web/wwwroot/appsettings.Development.json`
- Create: `apps/web/Properties/launchSettings.json`
- Create: `apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj`
- Create: `apps/web/Playdex.Web.Tests/ProjectContractTests.cs`
- Modify: `apps/api-dotnet/Playdex.sln`

**Step 1: Create the test project and write the failing project-contract test**

Create `Playdex.Web.Tests.csproj` first with the existing repository's xUnit package versions and a project reference to `../Playdex.Web.csproj`. Then create `ProjectContractTests.cs` with tests that locate the web project root and assert that:

```csharp
Assert.Contains("_framework/blazor.webassembly.js", File.ReadAllText(Path.Combine(root, "wwwroot", "index.html")));
Assert.Contains("<Router", File.ReadAllText(Path.Combine(root, "App.razor")));
Assert.DoesNotContain("react", File.ReadAllText(Path.Combine(root, "Playdex.Web.csproj")), StringComparison.OrdinalIgnoreCase);
Assert.Contains("Playdex.Web.Tests/**", File.ReadAllText(Path.Combine(root, "Playdex.Web.csproj")));
```

Use a single helper that walks upward from `AppContext.BaseDirectory` until `Playdex.Web.csproj` is found.

**Step 2: Run the test to verify it fails**

Run: `DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj`

Expected: FAIL because the referenced Blazor project does not exist yet.

**Step 3: Create the minimal host**

Use `Microsoft.NET.Sdk.BlazorWebAssembly`, target `net10.0`, enable nullable and implicit usings, and reference `Microsoft.AspNetCore.Components.WebAssembly` plus its development server at version `10.0.0`. Add `Playdex.Web.Tests/**` to `DefaultItemExcludes` so the nested xUnit sources, content, and build output can never be compiled or published by the web project. Register a scoped `HttpClient` in `Program.cs` and render `<App />` into `#app`.

`App.razor` must contain a `Router` whose `Found` block renders the route with `RouteView` and whose `NotFound` block renders `Home`. `Home.razor` initially contains only a parity placeholder that will be replaced in Task 6. `wwwroot/index.html` must include `<base href="/">`, the current body classes, `<link href="css/app.css" rel="stylesheet">`, the theme bootstrap in `<head>` before styles or Blazor startup, and `_framework/blazor.webassembly.js`.

Set the development launch URL to `http://127.0.0.1:5173`. Put an `__PLAYDEX_API_URL__` build placeholder in `appsettings.json` and the current browser-visible development value `https://app.playdex.orb.local/api/` in `appsettings.Development.json`. If the placeholder is still present in a non-Docker publish, fall back to same-origin `/api/`; never treat a container environment variable as directly visible to WebAssembly.

**Step 4: Add the project to the solution**

Add both new projects to `apps/api-dotnet/Playdex.sln` with `dotnet sln`.

**Step 5: Run the build and tests**

Run:

```bash
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet build apps/web/Playdex.Web.csproj
```

Expected: both commands PASS with zero errors.

**Step 6: Commit**

```bash
git add apps/web/Playdex.Web.csproj apps/web/Program.cs apps/web/_Imports.razor apps/web/App.razor apps/web/Pages/Home.razor apps/web/wwwroot apps/web/Properties apps/web/Playdex.Web.Tests apps/api-dotnet/Playdex.sln
git commit --only -m "feat: scaffold Blazor web application" -- apps/web/Playdex.Web.csproj apps/web/Program.cs apps/web/_Imports.razor apps/web/App.razor apps/web/Pages/Home.razor apps/web/wwwroot apps/web/Properties apps/web/Playdex.Web.Tests apps/api-dotnet/Playdex.sln
```

### Task 3: Port pure formatting, date, session, and game-hash behavior

**Files:**

- Create: `apps/web/Utilities/Formatters.cs`
- Create: `apps/web/Utilities/GameHash.cs`
- Create: `apps/web/Utilities/SessionDates.cs`
- Create: `apps/web/Playdex.Web.Tests/Utilities/FormattersTests.cs`
- Create: `apps/web/Playdex.Web.Tests/Utilities/GameHashTests.cs`
- Create: `apps/web/Playdex.Web.Tests/Utilities/SessionDatesTests.cs`
- Reference: `apps/web/src/utils/formatters.ts`
- Reference: `apps/web/src/utils/gameHash.ts`
- Reference: `apps/web/src/utils/sessions.ts`

**Step 1: Write failing characterization tests**

Cover exact React examples for:

- Base-62 round trips and case sensitivity using `id * 1_299_827 + 811`.
- Numeric game identifiers and invalid hashes.
- `en-US` integer, one-decimal, date, and duration output.
- Live session duration and ignored-session behavior.
- Current UTC Monday-through-Sunday date range.

Include boundary values: zero, one minute, one hour, Sunday, Monday, null image/value fields, and a hash containing both upper- and lower-case characters.

**Step 2: Run tests to verify they fail**

Run: `DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj --filter "FullyQualifiedName~Utilities"`

Expected: FAIL because the C# utilities do not exist.

**Step 3: Implement the smallest pure C# equivalents**

Port the TypeScript algorithms directly. Use invariant arithmetic, explicit UTC dates, and `CultureInfo.GetCultureInfo("en-US")`; do not replace established output with locale-dependent defaults.

**Step 4: Run tests**

Run the filtered command from Step 2.

Expected: PASS.

**Step 5: Commit**

```bash
git add apps/web/Utilities apps/web/Playdex.Web.Tests/Utilities
git commit --only -m "feat: port frontend utility behavior to C#" -- apps/web/Utilities apps/web/Playdex.Web.Tests/Utilities
```

### Task 4: Implement browser storage, authentication, and the raw .NET API client

**Files:**

- Create: `apps/web/Browser/IBrowserStorage.cs`
- Create: `apps/web/Browser/BrowserStorage.cs`
- Create: `apps/web/Browser/BrowserInterop.cs`
- Create: `apps/web/wwwroot/js/playdex.js`
- Create: `apps/web/Auth/AuthSession.cs`
- Create: `apps/web/Api/ApiException.cs`
- Create: `apps/web/Api/PlaydexApiClient.cs`
- Create: `apps/web/Models/AuthModels.cs`
- Create: `apps/web/Models/SessionModels.cs`
- Create: `apps/web/Models/StatsModels.cs`
- Create: `apps/web/Models/RecapModels.cs`
- Create: `apps/web/Models/GameModels.cs`
- Create: `apps/web/Models/ManualModels.cs`
- Create: `apps/web/Models/AdminModels.cs`
- Create: `apps/web/Models/ProviderModels.cs`
- Create: `apps/web/Models/SettingsModels.cs`
- Create: `apps/web/Playdex.Web.Tests/Auth/AuthSessionTests.cs`
- Create: `apps/web/Playdex.Web.Tests/Api/PlaydexApiClientTests.cs`
- Modify: `apps/web/Program.cs`
- Modify: `apps/web/wwwroot/index.html`
- Reference: `apps/web/src/api.ts`
- Reference: `apps/web/src/types.ts`
- Reference: `apps/api-dotnet/Playdex.Api/**/*Dto.cs`

**Step 1: Write failing auth-storage tests**

Use an in-memory `IBrowserStorage` fake. Assert login writes all four auth keys, logout removes them, `IsLoggedIn` depends on the access token, and admin is true only when `playdex.isAdmin == "1"`.

**Step 2: Write failing API-client tests**

Use a recording `HttpMessageHandler`. Cover:

```text
successful raw DTO deserialization
Authorization: Bearer <token>
one refresh call and one retry after 401
no second refresh loop
new access/refresh token persistence
Problem Details detail extraction
cancellation propagation
trailing slash normalization for the API base URL
leading-slash and non-leading-slash paths both stay under `/api`
exact final URIs for `/refresh`, `/me`, and `/sessions?title=a%20b&page=2`
absolute HTTP(S) request URLs pass through unchanged
unauthenticated login/refresh requests omit the bearer header
failed or missing refresh invokes the optional auth-failure callback once
```

Assert that the client does not require a `{ data, error }` envelope.

**Step 3: Run tests to verify they fail**

Run: `DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj --filter "FullyQualifiedName~Auth|FullyQualifiedName~Api"`

Expected: FAIL because services and models do not exist.

**Step 4: Implement storage and interop**

Keep the JavaScript module limited to local-storage get/set/remove, applying the `dark` class and `data-theme`, reading `prefers-color-scheme`, `window.confirm`, focus, and smooth scroll-to-top. All API and page logic stays in C#.

**Step 5: Implement auth and API behavior**

Build relative request URLs with normalized string concatenation (`apiBase.TrimEnd('/') + "/" + path.TrimStart('/')`), not `new Uri(base, path)`, so React-style leading slashes cannot discard `/api`. Preserve query strings and pass absolute HTTP(S) URLs through unchanged.

Serialize request bodies afresh for each attempt so retries are safe. On the first authenticated 401 only, call `/refresh` without a bearer token, update stored tokens, and retry the original request. If refresh is missing or fails, invoke the request's optional auth-failure callback exactly once; do not introduce an unconditional logout that React does not perform. On failure, throw `ApiException` with the status code and `detail` from Problem Details or the backend exception shape.

Mirror the backend's raw camel-case DTOs with nullable fields that match the endpoint records. Register storage, auth, browser interop, and the API client as scoped services.

**Step 6: Run tests**

Run the filtered command from Step 3, then the full web test project.

Expected: PASS.

**Step 7: Commit**

```bash
git add apps/web/Browser apps/web/Auth apps/web/Api apps/web/Models apps/web/wwwroot/js apps/web/Program.cs apps/web/wwwroot/index.html apps/web/Playdex.Web.Tests/Auth apps/web/Playdex.Web.Tests/Api
git commit --only -m "feat: add Blazor auth and API foundation" -- apps/web/Browser apps/web/Auth apps/web/Api apps/web/Models apps/web/wwwroot/js apps/web/Program.cs apps/web/wwwroot/index.html apps/web/Playdex.Web.Tests/Auth apps/web/Playdex.Web.Tests/Api
```

### Task 5: Build routing guards, theme bootstrap, Tailwind, and the shared shell

**Files:**

- Create: `apps/web/Routing/NavigationGuard.cs`
- Create: `apps/web/Layout/AppLayout.razor`
- Create: `apps/web/Components/AppHeader.razor`
- Create: `apps/web/Components/AppFooter.razor`
- Create: `apps/web/Components/Background.razor`
- Create: `apps/web/Components/ThemeToggle.razor`
- Create: `apps/web/Components/StatsPieChart.razor`
- Create: `apps/web/Components/StatsRecapSection.razor`
- Create: `apps/web/Components/ManualEntryModal.razor`
- Create: `apps/web/Components/ManualSessionModal.razor`
- Create: `apps/web/Styles/app.css`
- Create: `apps/web/wwwroot/css/.gitignore`
- Create: `apps/web/Playdex.Web.Tests/Routing/NavigationGuardTests.cs`
- Modify: `apps/web/App.razor`
- Modify: `apps/web/Program.cs`
- Modify: `apps/web/package.json`
- Modify: `apps/web/tailwind.config.cjs`
- Modify: `apps/web/postcss.config.cjs`
- Reference: `apps/web/src/App.tsx`
- Reference: `apps/web/src/constants.ts`
- Reference: `apps/web/src/components/*.tsx`
- Reference: `apps/web/src/index.css`

**Step 1: Write failing navigation tests**

Cover this table:

| Path | Anonymous | User | Admin |
|---|---|---|---|
| `/` | allow | allow | allow |
| `/dashboard` | redirect `/` | allow | allow |
| `/sessions` | redirect `/` | allow | allow |
| `/recap` | redirect `/` | allow | allow |
| `/mapping` | redirect `/` | allow | allow |
| `/settings` | redirect `/` | allow | allow |
| `/games/abc` | redirect `/` | allow | allow |
| `/admin` | redirect `/` | redirect `/dashboard` after profile resolution | allow |
| unknown | home fallback | home fallback | home fallback |

Also assert that numeric game IDs, case-sensitive hashes, encoded characters, trailing slashes, and malformed hashes reach the Game page with the same normalized/null parameter React supplies. Assert that navigation to the current path is a no-op, normal navigation requests smooth scroll-to-top, and non-admin protected routes do not wait for `/me` profile resolution.

**Step 2: Run tests to verify they fail**

Run: `DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj --filter "FullyQualifiedName~Routing"`

Expected: FAIL.

**Step 3: Implement route and theme behavior**

Use Blazor's router and `NavigationManager`; do not recreate a second router. Resolve `/me` when logged in and cache `playdex.isAdmin`, but block profile resolution only for `/admin`, matching React. Other token-protected pages render immediately. Retain Home as the unknown-route fallback and use the Game route parameter normalization characterized above.

Apply the saved/system theme in a small inline bootstrap script before Blazor starts to prevent a light-theme flash. Theme changes must update `html.dark`, `data-theme`, and `playdex.theme`.

**Step 4: Add the parallel CSS pipeline without breaking the React baseline**

Copy the existing CSS directives unchanged into `Styles/app.css`. Add these exact scripts to `package.json`, but retain the React/Vite scripts and dependencies until Task 12 so the reference app remains runnable:

```json
"css:build": "tailwindcss -c tailwind.config.cjs -i ./Styles/app.css -o ./wwwroot/css/app.css --postcss --minify",
"css:watch": "tailwindcss -c tailwind.config.cjs -i ./Styles/app.css -o ./wwwroot/css/app.css --postcss --watch --poll"
```

Configure Tailwind content scanning for `./src/**/*.{ts,tsx}`, `./App.razor`, `./Pages/**/*.{razor,cs}`, `./Components/**/*.{razor,cs}`, `./Layout/**/*.{razor,cs}`, and `./wwwroot/{index.html,js/*.js}`. Do not scan `node_modules`. Include `.cs` because some class literals are selected in code-behind files, and safelist any dynamic class assembled from fragments. Retain `darkMode: "class"`. Put `*` and `!.gitignore` in `wwwroot/css/.gitignore` so generated CSS is built, not committed.

Run `bun install` after changing `package.json` so `bun.lock` is synchronized.

Run: `bun run css:build`

Expected: `wwwroot/css/app.css` is generated and contains the current responsive and dark variants.

**Step 5: Port shared components literally**

Copy the existing element hierarchy and class strings into Razor. Replace only React syntax (`className`, event callbacks, fragments, portals) with Razor equivalents. Copy `BarChart3` as inline SVG; add no icon or component package.

**Step 6: Run tests and build**

Run:

```bash
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj
bun run css:build
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet build apps/web/Playdex.Web.csproj
```

Expected: PASS.

**Step 7: Compare the shell visually**

Render a temporary protected-page shell with fixture content and compare header, footer, background, theme toggle, charts, and both modals at 390×844 and 1440×900 in light and dark themes. Record results in `docs/visual-parity/react-baseline.md`.

**Step 8: Commit**

```bash
git add apps/web/Routing apps/web/Layout apps/web/Components apps/web/Styles apps/web/wwwroot apps/web/App.razor apps/web/Program.cs apps/web/package.json apps/web/bun.lock apps/web/tailwind.config.cjs apps/web/postcss.config.cjs apps/web/Playdex.Web.Tests/Routing docs/visual-parity/react-baseline.md
git commit --only -m "feat: port Blazor routing and shared shell" -- apps/web/Routing apps/web/Layout apps/web/Components apps/web/Styles apps/web/wwwroot apps/web/App.razor apps/web/Program.cs apps/web/package.json apps/web/bun.lock apps/web/tailwind.config.cjs apps/web/postcss.config.cjs apps/web/Playdex.Web.Tests/Routing docs/visual-parity/react-baseline.md
```

### Task 6: Port Home and Dashboard

**Files:**

- Modify: `apps/web/Pages/Home.razor`
- Create: `apps/web/Pages/Dashboard.razor`
- Create: `apps/web/Pages/Dashboard.razor.cs`
- Create: `apps/web/Playdex.Web.Tests/Pages/HomeStateTests.cs`
- Create: `apps/web/Playdex.Web.Tests/Pages/DashboardStateTests.cs`
- Reference: `apps/web/src/pages/HomePage.tsx`
- Reference: `apps/web/src/pages/DashboardPage.tsx`

**Step 1: Write failing page-state tests**

Extract only test-worthy state transformations into internal C# helpers. Cover login disable/error/success state, profile fallback, Dashboard all-or-nothing loading, today and three stats ranges, current-session display, and silent refresh after manual start/stop.

**Step 2: Run tests to verify they fail**

Run: `DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj --filter "FullyQualifiedName~HomeState|FullyQualifiedName~DashboardState"`

Expected: FAIL.

**Step 3: Translate Home markup and behavior**

Port one visible region at a time in this order: header/theme control, hero and login form, feature/preview grid, CTA, footer. After each region, run `dotnet build apps/web/Playdex.Web.csproj` and compare that region at 390×844 and 1440×900 before continuing. Preserve every preview card, gradient placeholder, login field, button state, copy string, and responsive class. Login must write the existing auth keys and perform the same hard navigation to `/dashboard`.

**Step 4: Translate Dashboard markup and behavior**

Port one region at a time: page shell, current-session card, today summary, three stats ranges, recap/charts, recent-session content, manual-session trigger, modal. Build after each region. Keep the concurrent request set, aggregate error behavior, chart calculations, current/manual session sections, and modal interactions. Do not add polling; current duration updates only when the component rerenders, matching React.

**Step 5: Run tests, CSS build, and web build**

Run from the repository root, except the CSS command whose working directory is `apps/web`:

```bash
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj --filter "FullyQualifiedName~HomeState|FullyQualifiedName~DashboardState"
bun run css:build
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet build apps/web/Playdex.Web.csproj
```

Expected: all commands PASS.

**Step 6: Perform visual comparison**

Compare Home and Dashboard at all three baseline viewports in both themes, including login error/loading and manual modal states. Update the matrix with pass/fail notes and fix every unexplained visual difference before committing.

**Step 7: Commit**

```bash
git add apps/web/Pages/Home.razor apps/web/Pages/Dashboard.razor apps/web/Pages/Dashboard.razor.cs apps/web/Playdex.Web.Tests/Pages docs/visual-parity/react-baseline.md
git commit --only -m "feat: port Home and Dashboard to Blazor" -- apps/web/Pages/Home.razor apps/web/Pages/Dashboard.razor apps/web/Pages/Dashboard.razor.cs apps/web/Playdex.Web.Tests/Pages docs/visual-parity/react-baseline.md
```

### Task 7: Port Sessions and Recap

**Files:**

- Create: `apps/web/Pages/Sessions.razor`
- Create: `apps/web/Pages/Sessions.razor.cs`
- Create: `apps/web/Pages/Recap.razor`
- Create: `apps/web/Pages/Recap.razor.cs`
- Create: `apps/web/Playdex.Web.Tests/Pages/SessionsStateTests.cs`
- Create: `apps/web/Playdex.Web.Tests/Pages/RecapStateTests.cs`
- Reference: `apps/web/src/pages/SessionsPage.tsx`
- Reference: `apps/web/src/pages/RecapPage.tsx`

**Step 1: Write failing state tests**

Cover Sessions UTC-week defaults, 300 ms title debounce cancellation, title/date/sort page reset, stats ignoring title, deletion confirmation outcome, mobile/desktop data equivalence, and manual-entry refresh. Cover Recap all-time/year/custom modes, both-query-values requirement, URL construction, year loading, and game navigation.

**Step 2: Verify tests fail**

Run: `DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj --filter "FullyQualifiedName~SessionsState|FullyQualifiedName~RecapState"`

Expected: FAIL on the first unimplemented characterization.

**Step 3: Port Sessions literally**

Port one region at a time: filters, stats recap, loading/error/empty states, mobile cards, desktop table, pagination, deletion confirmation, Manual Entry modal. Build after each region. Retain separate mobile cards and desktop tables at the `md` breakpoint. Preserve filters, sort indicators, ignored/mapped badges, and behavior.

**Step 4: Port Recap literally**

Port one region at a time: range tabs/custom form, hero totals, chart blocks, rankings/game cards, loading/error/empty states. Build after each region. Preserve query-backed filters, tab state, capsule images, totals, and navigation. Use `NavigationManager.GetUriWithQueryParameters` or an equivalent explicit builder that emits both `from` and `to` together.

**Step 5: Run tests/build and compare visuals**

Run the exact filtered test from Step 2, `bun run css:build` from `apps/web`, and `dotnet build apps/web/Playdex.Web.csproj` from the repository root. Expected: PASS. Then compare both routes across the full viewport/theme matrix plus filter, empty, error, and modal states. Update the parity document.

**Step 6: Commit**

```bash
git add apps/web/Pages/Sessions.razor apps/web/Pages/Sessions.razor.cs apps/web/Pages/Recap.razor apps/web/Pages/Recap.razor.cs apps/web/Playdex.Web.Tests/Pages/SessionsStateTests.cs apps/web/Playdex.Web.Tests/Pages/RecapStateTests.cs docs/visual-parity/react-baseline.md
git commit --only -m "feat: port Sessions and Recap to Blazor" -- apps/web/Pages/Sessions.razor apps/web/Pages/Sessions.razor.cs apps/web/Pages/Recap.razor apps/web/Pages/Recap.razor.cs apps/web/Playdex.Web.Tests/Pages/SessionsStateTests.cs apps/web/Playdex.Web.Tests/Pages/RecapStateTests.cs docs/visual-parity/react-baseline.md
```

### Task 8: Port Mapping and Settings

**Files:**

- Create: `apps/web/Pages/Mapping.razor`
- Create: `apps/web/Pages/Mapping.razor.cs`
- Create: `apps/web/Pages/Settings.razor`
- Create: `apps/web/Pages/Settings.razor.cs`
- Create: `apps/web/Playdex.Web.Tests/Pages/MappingStateTests.cs`
- Create: `apps/web/Playdex.Web.Tests/Pages/SettingsStateTests.cs`
- Reference: `apps/web/src/pages/MappingPage.tsx`
- Reference: `apps/web/src/pages/SettingsPage.tsx`

**Step 1: Write failing state tests**

Cover Mapping initial parallel loads, explicit-button searches, one-row-at-a-time edit state, map/connect/disconnect refresh, and selection clearing. Cover Settings independent provider/settings failures, hidden `manual` and `local` providers, optimistic/disabled button states, and non-negative integer validation.

**Step 2: Verify tests fail**

Run: `DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj --filter "FullyQualifiedName~MappingState|FullyQualifiedName~SettingsState"`

Expected: FAIL on the first unimplemented characterization.

**Step 3: Port both pages literally**

For Mapping, port initial lists, manual-input search/edit, game search, connection cards/table, and mutation states one region at a time. For Settings, port provider rows/cards, setting form, validation, and independent error/success states one region at a time. Build after each region. Preserve the responsive mobile-card/desktop-table markup, form labels, buttons, badges, error placement, and all existing class strings. Use `EditForm` only where it does not change emitted structure or native validation behavior; otherwise keep explicit handlers.

**Step 4: Run tests/build and compare visuals**

Run the exact filtered test from Step 2, `bun run css:build` from `apps/web`, and `dotnet build apps/web/Playdex.Web.csproj` from the repository root. Expected: PASS. Compare default, search, editing, disabled, success, and error states at the baseline viewports and themes. Update the parity document.

**Step 5: Commit**

```bash
git add apps/web/Pages/Mapping.razor apps/web/Pages/Mapping.razor.cs apps/web/Pages/Settings.razor apps/web/Pages/Settings.razor.cs apps/web/Playdex.Web.Tests/Pages/MappingStateTests.cs apps/web/Playdex.Web.Tests/Pages/SettingsStateTests.cs docs/visual-parity/react-baseline.md
git commit --only -m "feat: port Mapping and Settings to Blazor" -- apps/web/Pages/Mapping.razor apps/web/Pages/Mapping.razor.cs apps/web/Pages/Settings.razor apps/web/Pages/Settings.razor.cs apps/web/Playdex.Web.Tests/Pages/MappingStateTests.cs apps/web/Playdex.Web.Tests/Pages/SettingsStateTests.cs docs/visual-parity/react-baseline.md
```

### Task 9: Port Game and Admin

**Files:**

- Create: `apps/web/Pages/Game.razor`
- Create: `apps/web/Pages/Game.razor.cs`
- Create: `apps/web/Pages/Admin.razor`
- Create: `apps/web/Pages/Admin.razor.cs`
- Create: `apps/web/Playdex.Web.Tests/Pages/GameStateTests.cs`
- Create: `apps/web/Playdex.Web.Tests/Pages/AdminStateTests.cs`
- Reference: `apps/web/src/pages/GamePage.tsx`
- Reference: `apps/web/src/pages/AdminPage.tsx`

**Step 1: Write failing Game tests**

Cover hash/numeric route input, paged loading of all sessions from 2025 onward, ignored-session exclusion, yearly/monthly aggregation, rating/ignore mutations, five-row local pagination, empty chart ranges, and cancellation of stale loads.

**Step 2: Write failing Admin tests**

Cover all-or-nothing initial loading, admin-only guard, platform toggles, per-credential drafts and saves, session search/selection/move, 25-row pagination, and game search/edit state.

**Step 3: Verify tests fail**

Run: `DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj --filter "FullyQualifiedName~GameState|FullyQualifiedName~AdminState"`

Expected: FAIL on the first unimplemented characterization.

**Step 4: Port Game literally**

Port one region at a time: hero/background art, metric cards, yearly chart, monthly chart, rating/ignored controls, recent mobile cards, recent desktop table, pagination, error/empty states. Build after each region. Preserve current client-side calculations exactly.

**Step 5: Port Admin literally**

Port one region at a time: platform status, credential drafts, recent sessions, movable-session search/selection/pagination, move form, game search/editor, global loading/error states. Build after each region. Retain all form drafts, status indicators, pagination, selection state, and disabled/loading/error variants. Do not broaden admin authorization; the backend remains authoritative.

**Step 6: Run tests/build and compare visuals**

Run the exact filtered test from Step 3, `bun run css:build` from `apps/web`, and `dotnet build apps/web/Playdex.Web.csproj` from the repository root. Expected: PASS. Compare both pages in all baseline viewports/themes and their key interactive states. Update the parity document.

**Step 7: Commit**

```bash
git add apps/web/Pages/Game.razor apps/web/Pages/Game.razor.cs apps/web/Pages/Admin.razor apps/web/Pages/Admin.razor.cs apps/web/Playdex.Web.Tests/Pages/GameStateTests.cs apps/web/Playdex.Web.Tests/Pages/AdminStateTests.cs docs/visual-parity/react-baseline.md
git commit --only -m "feat: port Game and Admin to Blazor" -- apps/web/Pages/Game.razor apps/web/Pages/Game.razor.cs apps/web/Pages/Admin.razor apps/web/Pages/Admin.razor.cs apps/web/Playdex.Web.Tests/Pages/GameStateTests.cs apps/web/Playdex.Web.Tests/Pages/AdminStateTests.cs docs/visual-parity/react-baseline.md
```

### Task 10: Resolve verified backend parity blockers

**Files:**

- Modify if proven necessary: `apps/api-dotnet/Playdex.Api/Accounts/AccountsRepository.cs`
- Modify if proven necessary: `apps/api-dotnet/Playdex.Api/Manual/ManualDto.cs`
- Modify if proven necessary: `apps/api-dotnet/Playdex.Api/Manual/ManualService.cs`
- Modify if proven necessary: `apps/api-dotnet/Playdex.Api/Manual/ManualRepository.cs`
- Test: `apps/api-dotnet/Playdex.Api.Tests/*Tests.cs`
- Modify: `specs/002-dotnet-migration/verification.md`

**Step 1: Add failing backend characterization tests**

Only add a test when the same live/fixture data produces different user-visible behavior from the React contract. Candidate failures are ignored/mapped session fields, IGDB result availability, and `igdbId` type consistency.

**Step 2: Run the focused backend test and verify it fails**

Run: `DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/api-dotnet/Playdex.Api.Tests/Playdex.Api.Tests.csproj --filter "FullyQualifiedName~<new test>"`

Expected: FAIL for the demonstrated parity gap.

**Step 3: Make the narrowest backend fix**

Follow `@dotnet-ef-patterns`, `@dotnet-service-patterns`, and `@dotnet-endpoint-patterns`. Prefer existing LINQ/repository conventions, preserve cancellation, and do not refactor unrelated migration work.

**Step 4: Run focused and full backend tests**

Expected: PASS.

**Step 5: Record remaining external blockers**

Update `specs/002-dotnet-migration/verification.md` with evidence for anything that depends on credentials, external IGDB availability, or unavailable production data.

**Step 6: Commit only files changed for proven blockers**

Use an explicit path list. If no blocker is proven, make no backend commit.

### Task 11: Switch development, Docker, and CI to Blazor

**Files:**

- Modify: `apps/web/Dockerfile`
- Modify: `apps/web/Dockerfile.dev`
- Modify: `apps/web/nginx.conf` only if published Blazor assets require it
- Modify: `docker-compose.yml`
- Modify: `.github/workflows/build.yaml`
- Modify: `.github/workflows/test.yaml`
- Modify: `apps/web/package.json`
- Modify: `apps/web/bun.lock`
- Create: `apps/web/.dockerignore`
- Create: `apps/web/README.md`
- Create: `apps/web/Playdex.Web.Tests/DeploymentContractTests.cs`

**Step 1: Write failing deployment contract tests**

Assert that the production Dockerfile contains a .NET 10 SDK publish stage, copies `wwwroot` output from `dotnet publish`, retains nginx, and no longer runs `vite build`. Assert that nginx retains SPA fallback, listens on the current application port `5056`, the Dockerfile exposes `5056`, and the image health check targets the same port. Treat MIME behavior as an integration assertion, not a source-text assertion.

**Step 2: Verify the test fails**

Run the deployment-filtered web tests and expect FAIL against the React Dockerfile.

**Step 3: Implement the production image**

Use one Bun stage for `css:build`, one .NET 10 SDK stage for `dotnet publish -c Release`, and the existing nginx runtime. Copy the generated CSS from the Bun stage into the publish input. Accept `PLAYDEX_API_URL` as a Docker **build argument**, replace `__PLAYDEX_API_URL__` in `appsettings.json` before publish, and preserve `try_files $uri /index.html`. Do not claim that a runtime container environment variable is visible inside static WebAssembly. Align nginx `listen`, Docker `EXPOSE`, and the health check on port `5056` to preserve the actual current server contract.

**Step 4: Implement the development image and Compose settings**

Base the development image on the .NET 10 SDK and copy the Bun executable from an `oven/bun:1` stage so both `dotnet watch` and Tailwind are available without installing tools at container startup. Run CSS watch and `dotnet watch --project Playdex.Web.csproj --urls http://0.0.0.0:5173` through a small shell command and mount NuGet and Bun caches separately from the source bind mount. The bind-mounted `appsettings.Development.json` supplies `https://app.playdex.orb.local/api/`; remove `VITE_API_URL` from the development Compose build because an environment/ARG cannot override static browser configuration after the bind mount is applied.

**Step 5: Update CI**

Install .NET 10 and Bun, run the CSS build, run `dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj`, and publish/build the web project. Change the UI image build argument to `PLAYDEX_API_URL`.

**Step 6: Verify locally**

Run:

```bash
bun run css:build
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet publish apps/web/Playdex.Web.csproj -c Release -o .tmp/blazor-publish
docker compose build ui
docker compose up -d ui
docker compose ps
docker build --build-arg PLAYDEX_API_URL=https://app.playdex.orb.local/api/ -t playdex-ui-blazor apps/web
docker run --rm -d --name playdex-ui-blazor -p 5056:5056 playdex-ui-blazor
curl -fsS http://127.0.0.1:5056/
curl -fsS http://127.0.0.1:5056/sessions
curl -fsSI http://127.0.0.1:5056/_framework/dotnet.native.wasm
docker stop playdex-ui-blazor
```

Expected: all build/test commands pass, both development and production UI containers run, `/` renders, direct navigation to `/sessions` returns the SPA rather than nginx 404, and the `.wasm` response includes `Content-Type: application/wasm`.

**Step 7: Commit**

```bash
git add apps/web/Dockerfile apps/web/Dockerfile.dev apps/web/nginx.conf apps/web/.dockerignore apps/web/README.md apps/web/package.json apps/web/bun.lock apps/web/Playdex.Web.Tests/DeploymentContractTests.cs .github/workflows/build.yaml .github/workflows/test.yaml
git commit --only -m "build: switch web delivery to Blazor" -- apps/web/Dockerfile apps/web/Dockerfile.dev apps/web/nginx.conf apps/web/.dockerignore apps/web/README.md apps/web/package.json apps/web/bun.lock apps/web/Playdex.Web.Tests/DeploymentContractTests.cs .github/workflows/build.yaml .github/workflows/test.yaml
```

### Task 12: Remove React artifacts and close every verification gate

**Files:**

- Delete: `apps/web/src/**/*.ts`
- Delete: `apps/web/src/**/*.tsx`
- Delete: `apps/web/tests/AppFooter.test.tsx`
- Delete: `apps/web/vite.config.ts`
- Delete: `apps/web/tsconfig.json`
- Delete: `apps/web/eslint.config.mjs`
- Delete: `apps/web/index.html`
- Delete: `apps/web/src/index.css`
- Modify: `apps/web/package.json`
- Modify: `apps/web/bun.lock`
- Modify: `docs/visual-parity/react-baseline.md`
- Create: `docs/visual-parity/blazor-verification.md`

**Step 1: Prove the Blazor replacement is complete**

Before deleting anything, verify every React route has a corresponding Razor `@page` directive and every shared React component has either a Razor equivalent or a documented reason it was inlined.

Run:

```bash
rg -n "@page" apps/web/Pages
rg -n "react|react-dom|vite|lucide-react" apps/web --glob '!bun.lock' --glob '!docs/**'
```

Expected: all eight route families are present; runtime React/Vite references remain only in files scheduled for deletion.

**Step 2: Start React and Blazor side by side**

Keep every React source and dependency in place. Start the React Vite app on port 5174 and the Blazor app on port 5173 so both use the same live API data:

Run the first two commands with working directory `apps/web`; run the third from the repository root.

```bash
bun run dev -- --host 127.0.0.1 --port 5174
bun run css:watch
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet watch --project apps/web/Playdex.Web.csproj --urls http://127.0.0.1:5173
```

Run these as separate long-lived processes and record their exact URLs in the verification document.

**Step 3: Run the complete browser parity gate before deletion**

For each row in `docs/visual-parity/react-baseline.md`, open React on port 5174 and Blazor on port 5173 under identical API data, browser version, viewport, local-storage values, and theme. Capture Blazor screenshots, compare layout and interaction, and record PASS or the exact pixel/behavior difference in `docs/visual-parity/blazor-verification.md`.

Required interactive checks:

```text
login and logout
refresh-token retry
browser back/forward
theme persistence and no startup flash
protected/admin redirects
Sessions debounce, filters, pagination, deletion, manual entry
Recap URL filters
Dashboard manual session start/stop
Mapping map/connect/disconnect
Settings updates and validation
Game ignore/rating and charts
Admin toggles, credentials, session move, and game edit
direct deep links through nginx
```

Do not delete React while any matrix row is missing or failed. If external credentials or data make a row impossible, stop the cutover and report the blocker; a `pending` row is not one-to-one parity.

**Step 4: Delete obsolete React files and dependencies**

Only after Step 3 is fully green, stop the React server and remove the paths listed above. Regenerate `bun.lock` from the CSS-only `package.json`. Keep Tailwind, PostCSS, and Autoprefixer.

**Step 5: Run the complete automated gate**

Run the first two commands with working directory `apps/web`; run the remaining commands from the repository root.

```bash
bun install --frozen-lockfile
bun run css:build
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/web/Playdex.Web.Tests/Playdex.Web.Tests.csproj
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet test apps/api-dotnet/Playdex.sln
DOTNET_CLI_HOME=$PWD/.tmp/dotnet dotnet publish apps/web/Playdex.Web.csproj -c Release -o .tmp/blazor-publish
docker compose build ui
```

Expected: every command exits 0.

**Step 6: Confirm React is gone**

Run: `rg -n "react|react-dom|vite|lucide-react" apps/web --glob '!docs/**'`

Expected: no runtime or build references.

**Step 7: Commit cleanup and verification**

```bash
git add apps/web docs/visual-parity
git commit --only -m "feat: complete React to Blazor cutover" -- apps/web docs/visual-parity
```

### Task 13: Final independent review

**Files:**

- Review: every path changed by Tasks 1–12
- Modify only if review finds a defect

**Step 1: Request a code review**

Use `@superpowers:requesting-code-review` with emphasis on API retry correctness, auth leakage, cancellation, route guards, Razor rendering keys, unsafe markup, date/time equivalence, responsive duplicate markup, and preservation of the dirty .NET migration work.

**Step 2: Address findings with TDD**

For each confirmed defect, add or strengthen a failing test, implement the minimal correction, and rerun the focused test.

**Step 3: Run verification-before-completion**

Use `@superpowers:verification-before-completion` and rerun the full commands from Task 12 without relying on earlier results.

**Step 4: Inspect the final diff**

Run:

```bash
git status --short
git diff --stat codex/net...HEAD
git diff --check
```

Expected: no accidental staging or whitespace errors; unrelated pre-existing changes remain preserved.

**Step 5: Commit review fixes if any**

Use explicit paths and a focused message. Do not create an empty commit.
