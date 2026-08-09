# Playdex Blazor UI

The production UI is a .NET 10 Blazor WebAssembly application. React and Vite
were removed after the live visual-parity gate passed.

## Local development

```bash
bun install --frozen-lockfile
bun run css:build
dotnet watch --project Web.csproj --urls http://127.0.0.1:5173
```

`wwwroot/appsettings.Development.json` supplies the browser-visible API base
URL. Tailwind scans Razor, code-behind, layout, component, and static host
files; generated CSS is written to `wwwroot/css/app.css` and is not committed.

## Verification

```bash
bun run css:build
dotnet test Web.Tests/Web.Tests.csproj
dotnet publish Web.csproj -c Release
docker build --build-arg PLAYDEX_API_URL=https://example.test/api/ .
```

The Docker build argument is compiled into static `appsettings.json`. A runtime
container environment variable cannot change the API URL after publication.
