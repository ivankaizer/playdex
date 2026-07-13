# React to Blazor Migration Design

**Status:** Approved  
**Date:** 2026-07-13  
**Branch:** `codex/blazor-migration`

## Objective

Replace the React/Vite application in `apps/web` with a standalone .NET 10 Blazor WebAssembly application while preserving the current user interface, responsive behavior, browser state, routes, and interactions with one-to-one visual parity.

The migration is a frontend implementation change, not a redesign. Existing wording, spacing, colors, typography, breakpoints, loading states, empty states, error states, dialogs, charts, navigation rules, and light/dark variants remain the reference contract.

## Chosen Architecture

Use a standalone Blazor WebAssembly SPA in `apps/web` and continue serving its static published output from the existing nginx UI container.

This keeps the current deployment boundary between the browser application and `Playdex.Api`, preserves browser-side bearer authentication and local storage, and avoids introducing server rendering or SignalR behavior that the React application does not have.

The rejected alternatives are:

- Hosting the WebAssembly application from `Playdex.Api`: fewer deployment artifacts, but it couples the UI cutover to the unfinished backend migration and changes the current container boundary.
- A Blazor Web App with interactive server or auto rendering: adds prerendering, circuit, authentication-bootstrap, and local-storage complexity without a parity benefit.

## Application Structure

The Blazor application will use these layers:

- `Layout` and shared components for the header, footer, background, theme toggle, charts, recap cards, and manual-session dialogs.
- Routable Razor components for `/`, `/dashboard`, `/sessions`, `/recap`, `/mapping`, `/settings`, `/admin`, and `/games/{hash}`.
- A small authentication state service that owns the existing `playdex.token`, `playdex.refresh`, `playdex.email`, and `playdex.isAdmin` local-storage contract.
- A typed API client that adds bearer tokens, performs one refresh-and-retry after a 401, deserializes the .NET API's raw camel-case DTOs, and reads Problem Details errors.
- Narrow JavaScript interop for browser-only facilities: local storage, initial theme selection, root document classes, media queries, confirmation dialogs, focus/scroll behavior, and any portal-style modal behavior that cannot be expressed directly in Razor.
- Pure C# utilities for game hash encoding/decoding, UTC date ranges, duration/number formatting, filtering, sorting, and chart calculations.

Page state remains local to each page unless the React implementation currently shares it. The migration will not introduce a global state framework.

## Visual Parity Strategy

Razor markup will be a literal translation of the existing React DOM and Tailwind class strings. Tailwind 3.4 remains the CSS compiler and will scan `.razor`, `.html`, and the small JavaScript interop surface. The existing Space Grotesk font, 15 px mobile root size, class-based dark mode, remote game artwork, inline SVG charts, responsive breakpoints, fixed modal layers, mobile card layouts, and desktop table layouts will be retained.

The only icon imported from `lucide-react`, `BarChart3`, will become the equivalent static inline SVG. No Blazor component library or replacement design system will be added.

One-to-one parity means:

- The same content and controls appear at the same routes and authentication states.
- Layout, typography, spacing, colors, borders, shadows, imagery, charts, and responsive transitions match at mobile, tablet, and desktop widths.
- Light and dark themes match and persist under the existing `playdex.theme` key without a visible startup flash.
- Loading, empty, error, disabled, hover/focus, modal, authenticated, unauthenticated, admin, and non-admin states remain visually and behaviorally equivalent.

## Routing and Browser Behavior

Blazor routing will preserve the current URL surface. Protected pages redirect unauthenticated users to `/`; `/admin` redirects authenticated non-admin users to `/dashboard`; unknown paths render the home page. Game detail URLs retain the case-sensitive base-62 hash behavior, including acceptance of numeric identifiers.

The recap page keeps its `from` and `to` query-string behavior. Other page filters remain local state. Browser back/forward navigation, smooth scrolling, the Sessions page 300 ms search debounce, deletion confirmation, login/logout navigation, and modal focus behavior will match the React implementation.

## API Contract

The Blazor client will target the current .NET endpoints under `/api`. Unlike the React client, which expects a `{ data, error }` envelope, the .NET endpoints return raw DTOs and Problem Details. This difference will be isolated inside one API client rather than leaked into page components.

The frontend DTO set will mirror the .NET records and use the same JSON property names and nullability. API base configuration will support local development and the Docker value currently supplied as `VITE_API_URL`, renamed to a Blazor-compatible runtime/build setting.

Known backend parity blockers will be verified separately during the cutover: session mapping currently hardcodes ignored/mapped fields, IGDB manual search is disabled, and manual-search `igdbId` types are inconsistent. These must not be hidden with frontend-only workarounds. Any required backend changes will be narrowly scoped and covered by backend tests.

## Build and Deployment

`apps/web` will become a .NET 10 project. The build produces Tailwind CSS and then runs `dotnet publish`. Docker will use SDK and CSS-build stages followed by the existing nginx runtime and SPA fallback configuration. Development will use `dotnet watch` for the Blazor app while retaining the same API base and browser-accessible route behavior.

The web project will be added to the solution and CI. React, Vite, React-specific tests, and unused JavaScript dependencies will be removed only after the Blazor replacement builds and the parity checks pass.

## Verification

Verification has four gates:

1. Build and unit tests for the solution, shared C# utilities, authentication/token refresh, DTO serialization, and page-state transformations.
2. Route and interaction checks for every page, authentication/admin guard, query strings, modals, pagination, filtering, mutations, and browser back/forward behavior.
3. Visual comparison against the React baseline for all routes in light and dark themes at representative mobile, tablet, and desktop viewports, including key loading, empty, error, and modal states.
4. Container and CI checks proving nginx deep-link fallback, runtime API configuration, Docker startup, and the absence of React runtime artifacts.

Visual differences are defects unless they are caused by browser-native rendering below the agreed screenshot tolerance. Functional differences require either correction or an explicit backend blocker recorded in the migration verification document.

## Migration Sequence

1. Preserve React screenshots and enumerate observable states before deleting source files.
2. Scaffold the minimal Blazor WebAssembly host, CSS pipeline, routing, auth storage, and API client.
3. Port the shared shell and reusable visual components.
4. Port pages in independent vertical slices, preserving their existing API calls and state transitions.
5. Switch Docker, development, solution, and CI integration to Blazor.
6. Run automated and browser visual verification, address parity gaps, then remove obsolete React/Vite artifacts.

## Scope Guardrails

- No redesign, copy rewrite, component library, state framework, SSR, PWA, or unrelated backend refactor.
- Reuse the existing class strings, HTML structure, SVGs, DTO names, routes, and browser storage contract wherever possible.
- Keep JavaScript interop small and browser-specific; application logic belongs in C#.
- Preserve unrelated working-tree changes already present when this branch was created.
