# Administration topology and operations

Playdex consists of three independently deployable applications: API (`:3000`), customer Web (`:5173` in local development), and Admin (`:3100`). Admin is an Interactive Server Blazor application using the free `Radzen.Blazor` package. It reads and mutates the shared PostgreSQL database directly and never calls the API or customer Web application.

## Deployment order

1. Deploy PostgreSQL and apply the API migrations.
2. Verify API `/health/ready` and the database schema.
3. Deploy Admin with its dedicated origin, `DATABASE_URL`, and `Admin__AllowedOrigin`.
4. Verify Admin `/health/live`, `/health/ready`, authentication, and each workflow.

Admin readiness is read-only: it checks connectivity and required schema and never creates or alters database objects.

## Rollback

Redeploy the previous compatible API, Web, and Admin images. This change introduces no database migration, so rollback does not require database rollback.

## PostgreSQL grants

Use a separate Admin database role in production. Grant only `CONNECT` and access to the tables required by the allowlisted Dashboard, Integrations, Sessions, and Games workflows. Do not grant schema ownership, DDL privileges, migration-table write access, or unrestricted SQL execution. The API role remains the sole migration owner.

## Local development

```sh
docker compose up db app admin
dotnet run --project src/Admin/Admin.csproj
```

Set `DATABASE_URL` to the local database URL. No Radzen license key or paid Studio feature is required.

## Accepted Admin origin

Local compose deployment: `http://localhost:3100`. Production is expected to use a dedicated origin such as `admin.<domain>`; set `Admin__AllowedOrigin` and the ingress hostname to match, then record the accepted origin here.

## Operator acceptance checklist

Verified against the composed deployment on `http://localhost:3100` with a seeded administrator account:

- [x] `/health/live` and `/health/ready` report healthy.
- [x] `/login` renders and accepts administrator credentials.
- [x] Invalid, missing, or non-admin credentials fail with the generic `?failed=1` redirect.
- [x] Successful login issues the `playdex.admin` HTTP-only, same-site, bounded-lifetime cookie and redirects to `/`.
- [x] Anonymous navigation to `/`, `/integrations`, `/sessions`, and `/games` redirects to login.
- [x] Authenticated navigation to Dashboard, Integrations, Sessions, and Games loads each workflow page.
- [x] Logout invalidates the cookie and redirects to `/login`.
- [ ] Dashboard shows the last three sessions per user with duration and live-state presentation.
- [ ] Integrations enable/disable and credential editing behave correctly.
- [ ] Sessions search, paging, selection, and bulk transfer behave correctly.
- [ ] Games search, metadata editing, and merge behave correctly.
