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
