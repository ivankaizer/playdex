# Playdex Admin

Playdex Admin is an independently deployable Interactive Server Blazor application. It uses the free MIT-licensed `Radzen.Blazor` package and accesses PostgreSQL only through server-side services and the shared persistence project.

Set `DATABASE_URL` to a PostgreSQL connection URL, then run:

```sh
dotnet restore src/Admin/Admin.csproj
dotnet run --project src/Admin/Admin.csproj
```

No Radzen Studio license or license key is required. The Admin process never runs database migrations; deploy the API first so its migration runner establishes a compatible schema. Configure `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, `Admin__AllowedOrigin`, and the cookie policy at the ingress boundary for production.
