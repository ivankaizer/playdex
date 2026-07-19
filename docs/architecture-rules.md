# Architecture Rules

These rules are intended to be transferable across projects. They describe
boundaries that AI agents and developers should preserve when changing the
codebase.

## Authorization Belongs At Route Boundaries

Authentication and authorization checks must be applied as middleware, guards,
or route-group decorators before a request reaches a business handler.

Handlers should receive already-resolved request identity or capability context,
for example `ctx.auth` or `ctx.admin`. They should not call helpers such as
`requireAuth(request)`, `requireAdmin(auth)`, or equivalent policy checks inline.

This keeps route policy visible at registration time, avoids duplicated checks,
and prevents new endpoints from accidentally skipping authorization.

Preferred shape:

```ts
app.use(
  withAdmin(new Elysia())
    .get("/api/admin/items", listItemsHandler)
    .post("/api/admin/items", createItemHandler)
);
```

Avoid this shape:

```ts
export const listItemsHandler = async (ctx) => {
  await requireAdmin(ctx.auth);
  return itemsService.list();
};
```

## Handlers Should Express Use Cases, Not Access Policy

Route handlers should parse request input, call application services, and return
HTTP responses. Cross-cutting policy such as authentication, role checks,
rate-limits, tenant resolution, and feature access should live in route-level
middleware or decorators.

If a handler needs the current user, tenant, or role, that value should already
be part of the typed handler context.

## Modules Own Their DTOs

Request and response DTOs belong to the feature module that defines the use case.
Shared HTTP packages may define transport mechanics such as `ApiResponse`,
`ApiErrorResponse`, pagination primitives, or common status envelopes, but they
must not become a bucket for module-specific contracts.

Preferred shape:

```ts
// modules/accounts/accounts.dto.ts
export type AccountSessionDto = {
  sessionId: number;
  game: string;
};

// modules/accounts/accounts.handlers.ts
import type { ApiResponse } from "../../app/http/types";
import type { AccountSessionDto } from "./accounts.dto";
```

Avoid this shape:

```ts
// app/http/types.ts
export type AccountSessionDto = {
  sessionId: number;
  game: string;
};
```

Keeping DTOs module-local makes ownership clear, prevents cross-module coupling
through a generic HTTP package, and makes it easier to move, split, or delete a
feature without editing a central type registry.

## Keep Policy Scopes Explicit

Middleware should be applied to the smallest route group that needs it. Do not
attach global authorization middleware when some routes use a different scheme,
such as API keys, webhooks, health checks, or public login routes.

Use isolated route groups for separate access models:

```ts
app.use(withAuth(new Elysia()).get("/api/me", meHandler));
app.use(withApiKey(new Elysia()).post("/api/webhooks/source", webhookHandler));
```

## Add Structural Tests For Boundary Rules

When a boundary rule exists, add tests that fail if code drifts back to the old
shape. For example, scan protected handlers to ensure they do not import or call
authorization helpers directly, and add route-level tests proving distinct auth
schemes do not overlap.

## Module Organization

The `Playdex.Api` project is organized as a modular monolith:

```
Shared/           — Infrastructure shared by all modules
  Persistence/    — DbContext, entities, migrations
  Http/           — Middleware, AppProblem, correlation ID
  Auth/           — JWT, claims extensions, auth endpoints
  Common/         — GameId, options, event bus

Modules/          — Feature modules
  Accounts/       — AccountsModule.cs, *Service.cs, *Repository.cs, *Dto.cs, *Endpoints.cs
  Games/          — Same pattern + public contract (IGames.cs)
  ...
```

**Module registration**: Each module exposes an `Add{Name}()` extension on
`IServiceCollection` and a `Map{Name}()` extension on `RouteGroupBuilder`.
`Program.cs` calls one line per module.

**Cross-module contracts**: If Module A depends on Module B, Module B exposes a
public interface (`IModuleB`) colocated in its directory. Module A injects the
interface, not the concrete type. Internal types (repositories, endpoints,
internal-only services) are not imported across module boundaries.

**Event bus**: Background services publish domain events via `EventBus.Publish()`.
Module handlers implement `IEventHandler<T>` and are registered in the module's
`Add{Name}()` method. This replaces `IServiceScopeFactory` service locator
patterns for cross-module background communication.
