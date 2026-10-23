# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

The UI is a React web app rendered full screen inside a Windows desktop host (WebView2). Its design language is the Playdex web identity at 10-foot scale, not native WinUI.

## Stack

- **Host:** .NET 10 (WinUI 3, Native AOT, MSIX).
- **UI:** React 19 + TypeScript + Vite, rendered in WebView2.

## Users

Playdex users who play on a Windows PC from the couch or on a handheld (ROG Ally, Legion Go and similar), usually with a controller. Their job: get from power-on to playing the right game in seconds, see their play history across every platform, and never think about where a game was bought.

## Product Purpose

The Playdex launcher is a console-style home screen for Windows. It replaces Steam Big Picture, Playnite Fullscreen and Hydra Big Picture as the place you land in Windows Full Screen Experience (FSE). It brings every installed PC game into one library, launches them, and records every session to Playdex automatically.

Success means:
- It is the fastest and best-looking way to start a game on a Windows gaming PC.
- Every session played through it shows up in Playdex without any manual step.

## Positioning

The only launcher whose library *is* your Playdex history. It knows what you play on PC, PlayStation, Xbox and Switch, so "continue playing", recaps and per-game stats cover everything, not just one storefront. Speed is a feature: it must feel faster than the console it imitates.

## Operating Context

- **How it starts:** registered as the FSE home app (MSIX, the `windows.gamingApp` extension, the gamingHome capability). It starts at boot and stays resident. Every return from a game or from the Game Bar "Home" action brings it back to the front.
- **Where it is used:** at TV distance (10-foot) or on a 7–8" handheld screen at arm's length.
- **Inputs:** an Xbox-layout controller first, plus PlayStation and Switch Pro controllers. Mouse and keyboard must also work well (a desktop PC on a TV). Touch only needs to be usable.
- **Game sources:**
  - Steam
  - Epic, GOG, EA and Ubisoft
  - Xbox / Game Pass for PC (Microsoft Store apps)
  - Emulators (RetroArch and other profiles) and manually added executables
- **Playdex link:**
  - A Playdex account is required, paired through a device-code flow.
  - Sessions are captured through the Playdex capture API (start / ping / end) and queued on disk while offline.
  - Tracking, Steam and SteamGridDB code is shared with the desktop app (`src/Playdex.Core`); the desktop app replaced the WinForms Wrapper.

## Capabilities and Constraints

- Performance budgets in `docs/launcher/performance.md` are binding: one-frame input response, virtualized lists, only transform and opacity animated, no live blur.
- **FSE requirements:**
  - Developer Mode plus a trusted signing certificate (no Store distribution yet).
  - Windows build 10.0.26100.7019 or later.
  - The handheld FSE edition.
- **Artwork:** game covers come from Playdex (IGDB/Steam), and hero, logo and icon art from SteamGridDB. Art may be missing, so the UI must fall back gracefully.
- **Undecided:**
  - Whether the Playdex API gains a library endpoint and multi-image art.
  - Whether non-Steam PC providers are accepted by the capture API (needed: `gog`, `epic`, `local`).
  - The in-game overlay (Guide button) scope.
  - Theming beyond light and dark.

## Brand Commitments

- **Name:** Playdex.
- **Visual identity is binding:** the Playdex v3 "cozy" identity from the web redesign (`ivankaizer/playdex` PR #29, `src/Frontend/src/styles/app.css`): frosted cards on a pastel mint/cyan/sand background, pill controls, Space Grotesk, spaced-caps kickers, and both light and dark modes. The launcher adapts it to 10-foot use; it does not replace it.

## Evidence on Hand

- Real Playdex API and preview environments with seeded demo data.
- Game art from Steam, IGDB and SteamGridDB.
- **Absent, do not fabricate:** user counts, testimonials, benchmarks against other launchers, pricing.

## Product Principles

1. **Instant or it's broken.** Speed is the reason to switch. A beautiful screen that drops frames fails.
2. **Your games, not a store.** Home is about what you play and what you were playing, never promotion.
3. **Every session counts.** Launching through Playdex must record playtime reliably, including offline. A lost session is a bug.
4. **One library, any source.** Steam, Epic, GOG, Xbox, emulators and loose exes are equal citizens.
5. **Controller-native, mouse-friendly.** Every action is reachable with a pad and visible focus. The mouse is never a second-class path.

## Accessibility & Inclusion

- Text contrast meets WCAG 2.2 AA in light and dark at 10-foot sizes.
- Focus is always visible and never conveyed by colour alone.
- Respect `prefers-reduced-motion`.
- Button prompts match the connected controller family (Xbox / PlayStation / Nintendo), with swappable confirm and back.
