# Game data providers beyond Steam and PSN

Research date: 2026-08-10

## Executive conclusion

There is no single API strategy that gives Playdex exact play sessions across consoles and PC stores.

- **For exact session start/end on Windows, extend the existing Playnite/Playdex wrapper.** Playnite already supports Steam, Epic, GOG, EA app, Battle.net and other libraries, and its extension API emits game-started and game-stopped events with elapsed time. This is the highest-value, lowest-risk path for Epic, GOG, Xbox PC/Game Pass, Ubisoft Connect, EA app, Battle.net, Amazon Games, itch.io and standalone executables.
- **Xbox is the only additional console worth prototyping as a direct online integration.** Official Microsoft authentication and read APIs can yield an achievement-derived title list, achievements, and possibly live presence. They do not yield a complete owned library or dependable historical sessions. An unofficial gateway such as OpenXBL is considerably easier, but adds vendor, policy and continuity risk.
- **Nintendo has no documented public consumer play-history API.** Treat screenshots and natural-language entry as the product path, especially screenshots from the Switch Parental Controls app, which contain daily per-game play time.
- **Epic Online Services and the GOG GALAXY SDK are not consumer-library APIs.** They are primarily product-scoped SDKs for game developers. Do not build a Playdex integration around them.
- **itch.io and RetroAchievements are useful exceptions.** itch.io has an official OAuth scope for owned games; RetroAchievements has a straightforward official API for recent games and achievement progress. Neither supplies exact general-purpose session intervals.

The recommended architecture is therefore hybrid: provider APIs for discovery/enrichment, local event capture for exact PC sessions, and a provider-neutral manual/chat/OCR importer for gaps.

## First separate the data products

“Game data integration” hides four different capabilities:

| Capability | What Playdex needs | Typical provider availability |
|---|---|---|
| Library/ownership | A list of titles the user owns or has claimed | Sometimes available, often launcher-local only |
| Cumulative playtime | A title and an all-time duration | Occasionally available; semantics vary |
| Achievements/recent activity | Evidence that a title was played and when | More common than ownership |
| Exact sessions | `started_at` and `ended_at` for every play event | Rarely available historically; best captured live |

Playdex stores exact intervals in `play_sessions`. A provider returning “37 hours played” cannot safely be converted into sessions. It should either remain a cumulative snapshot or be imported as explicitly approximate data. Likewise, an achievement timestamp proves activity near that time, not the duration of a session.

## Provider matrix

| Provider | Supported public integration | Library | Cumulative playtime | Achievements | Exact sessions | Playdex recommendation |
|---|---|---:|---:|---:|---:|---|
| Xbox | Official user OAuth plus Xbox services reads, but the useful surface is constrained/incomplete | Partial, achievement-derived | Inconsistent/title-defined | Yes | Live presence only; no history | Prototype after PC wrapper; optionally evaluate OpenXBL |
| Epic Games Store | No documented global consumer-library/playtime API; EOS is product-scoped | No via supported EOS | No | Only for the configured EOS product | No | Playnite/local launcher capture |
| GOG | GALAXY SDK is for a developer's own game; no supported consumer-library REST API found | No supported global API | No supported global API | Only own-title SDK context | No | Playnite/local capture; avoid private web endpoints |
| Nintendo Switch | No documented consumer API | No | UI only | Not generally exposed | No | Chat/text + Parental Controls screenshot import |
| itch.io | Official OAuth including `profile:owned` | Yes | No | No general platform system | No | Optional easy library import; wrapper for sessions |
| RetroAchievements | Official API key and public user endpoints | Recently played, not ownership | No dependable general total | Yes | No | Good optional enrichment/import |
| EA, Ubisoft, Battle.net, Amazon, Rockstar | No useful cross-title consumer play-history API found; Battle.net APIs are game/profile specific | Usually launcher-local | Usually launcher-local | Game-specific at best | No | Playnite/local capture |

“No supported API found” means that the platform did not document a consumer-facing interface for this purpose in the sources reviewed. It does not mean the vendor's own launcher has no internal endpoint.

## Xbox

### What is officially possible

Microsoft documents website authentication using Microsoft OAuth, an Xbox User Token and an XSTS token. A normal user grant can provide read access to Xbox data such as profile and stats; the app is registered in Microsoft Entra and requests Xbox sign-in scopes ([Microsoft: website authentication for Xbox services](https://learn.microsoft.com/en-us/gaming/gdk/docs/services/fundamentals/s2s-auth-calls/service-authentication/live-website-authentication)).

The most directly useful endpoint is title history. It returns titles for which a user has unlocked or progressed an achievement, and Microsoft explicitly warns that it is **not the user's full played/launched history** ([title-history endpoint](https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/live/rest/uri/titlehistory/uri-titlehistoryusersxuidhistorytitlesgetv2)). Its title records contain platform/title identifiers and achievement/gamerscore information, not ownership or a reliable general playtime field ([UserTitle JSON](https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/live/rest/json/json-usertitlev2)). Per-title achievements are readable as well ([achievements for title](https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/live/xsapi-c/achievements_c/functions/xblachievementsgetachievementsfortitleidasync)).

Xbox Real-Time Activity can notify a connected client about presence and other state changes ([RTA overview](https://learn.microsoft.com/en-us/gaming/gdk/docs/services/fundamentals/rta/live-real-time-activity-service-overview)). In principle, Playdex could turn “playing title X” transitions into live sessions. This would only capture activity while the integration is connected, is subject to privacy/presence behavior, and needs timeout/reconciliation logic. It cannot recover historical start/end times.

Microsoft Store collection APIs do not solve the global-library problem. Their service authentication is linked to the publisher's own Partner Center product, so they are suitable for checking a user's entitlement to **your product**, not enumerating every Xbox purchase ([Microsoft Store service authentication](https://learn.microsoft.com/en-us/gaming/gdk/docs/store/commerce/service-to-service/xstore-authenticating-your-service)). Xbox featured stats also do not give a uniform answer: stats are configured per title and access can be restricted ([featured stats overview](https://learn.microsoft.com/en-us/gaming/gdk/docs/services/player-data/stats-leaderboards/featured-stats/live-featuredstats-overview)).

### Feasibility

- **Official MVP: medium effort, partial value.** Link an Xbox account, sync the achievement-derived title history and achievement timestamps, and clearly label it incomplete. A later live-presence poller can create forward-only sessions.
- **OpenXBL MVP: easy, higher risk.** OpenXBL advertises profiles, achievements, presence, title history and playtime behind a simple API key, with a free tier ([OpenXBL](https://xbl.io/)). It is explicitly an unofficial gateway and does not control Xbox Live; its terms say endpoints may stop working when Microsoft changes upstream services ([OpenXBL terms](https://xbl.io/agreement)). It is suitable for a spike or opt-in beta, not a foundational dependency without a fallback/export path.
- **Xbox PC:** use the Windows wrapper/Playnite path instead. That produces actual process-based sessions and works independently of whether a title exposes Xbox stats.

## Epic Games Store

Epic Online Services (EOS) is public and mostly free, but it is a backend toolkit configured per developer organization, product, sandbox and deployment. Epic describes account, game and store services in the context of a developer's game ([EOS introduction](https://onlineservices.epicgames.com/en-US/news/introduction-to-epic-online-services-eos)). The SDK exposes achievements, stats, sessions, presence and commerce, but those interfaces operate in the configured EOS product/deployment ([EOS interface matrix](https://dev.epicgames.com/documentation/en-us/unreal-engine/enable-and-configure-online-services-eos-in-unreal-engine)). “Sessions” here means multiplayer/session-management objects, not a user's launcher play-history feed.

Epic's commerce interface can query entitlements in that product's store flow; it is intended to grant or verify the current game's products, not enumerate a user's entire Epic Games Store library ([Epic commerce interface](https://dev.epicgames.com/documentation/en-us/unreal-engine/commerce-interface-in-unreal-engine)). Epic also limits account data to permitted uses and requires services to be used through authorized SDK/product credentials ([EOS licensing](https://onlineservices.epicgames.com/licensing), [acceptable-use policy](https://onlineservices.epicgames.com/en-US/services/terms/aup)).

No supported global EGS consumer library, launcher playtime, or historical-session API was documented in the official surface reviewed. Implementing EOS login would therefore add consent/brand-review and credential work without returning the data Playdex primarily needs.

**Recommendation:** import Epic titles through Playnite and capture start/stop locally. A direct Legendary/Heroic or launcher-manifest adapter is possible, but duplicates library/auth work Playnite already handles and still does not solve exact sessions as cleanly.

## GOG

The official GOG GALAXY SDK exposes authentication, ownership checks, statistics and achievements for a developer integrating **their own title**. Achievements must be defined for that game in GOG's Developer Portal, then retrieved/stored through the SDK ([GOG statistics and achievements](https://docs.gog.com/sdk-stats-and-achievements/)). GOG documents the SDK as an identity provider that can verify authentication and licenses for purchased games in that title-integration context ([GALAXY features and operational states](https://docs.gog.com/sdk-galaxy-feats-and-states/)). This is not a supported global consumer-library API for a Playdex account connector.

GOG also publishes the GALAXY integrations Python API for plugins that feed owned/local games and activity into the GALAXY client ([GOG's integration API repository](https://github.com/gogcom/galaxy-integrations-python-api)). It is useful evidence that launcher-level aggregation is possible, but consuming GALAXY's local/private state would couple Playdex to implementation details rather than a supported user-data contract.

**Recommendation:** Playnite/local process capture for sessions and library discovery. Do not base a server poller on reverse-engineered GOG web endpoints or user cookies.

## Nintendo

Nintendo's public developer program provides platform tools for creating and publishing games; Switch information requires a separate approved access request and NDA ([Nintendo development process](https://developer.nintendo.com/the-process)). The reviewed public material does not expose a consumer library or play-history API.

Nintendo does expose useful information in first-party UI. The Switch Parental Controls app shows daily per-game play time and monthly activity summaries ([Nintendo Parental Controls](https://www.nintendo.com/us/mobile-apps/parental-controls/)). The console/profile UI also exposes approximate cumulative time, but that is not a session ledger.

### Recommended ingestion experience

1. Accept structured text such as “Zelda: Tears of the Kingdom, Switch, 2026-08-09 20:10–22:05”.
2. Accept screenshots from Parental Controls and OCR the date, title and duration. When a screenshot gives only a daily duration, store it as an approximate daily aggregate or ask for a start time; do not invent a precise interval.
3. Present a confirmation card before writing: resolved title, platform, local timezone, start/end or duration, and confidence/warnings.
4. Preserve the original text/image reference and extraction confidence for later correction/audit.

An MCP tool can expose this workflow to multiple assistants, but **MCP is the interface, not the parser or source of truth**. The core should be a normal authenticated Playdex ingestion service with validation, idempotency and provenance. A chatbot, web upload, Telegram bot, or MCP server should all call that same service. This also makes the flow useful for Xbox console and any provider outage, not just Nintendo.

## Other PC platforms

### The broad solution: Playnite

Playnite is open source and supports Steam, Epic, GOG, EA app, Battle.net and other libraries while keeping library data locally ([Playnite repository](https://github.com/JosefNemec/Playnite)). Its extension API exposes `OnGameStarted` and `OnGameStopped`; the stopped event includes elapsed seconds ([Playnite events](https://api.playnite.link/docs/tutorials/extensions/events.html)). That is exactly the semantic data Playdex needs for exact forward-looking sessions and avoids depending on every storefront's private authentication protocol.

This also covers Ubisoft Connect, Amazon Games, Rockstar, itch.io games, emulators and DRM-free executables whenever they are registered in Playnite. Provider-specific APIs can still enrich covers, ownership and achievements later.

### Useful public exceptions

- **itch.io:** third-party OAuth supports `profile:owned`, which lists games a user purchased or claimed. Loopback and copy/paste flows are documented for desktop apps ([itch.io OAuth](https://itch.io/docs/api/oauth)). This is an easy, supported library import. It does not expose platform-wide playtime or sessions, so pair it with the wrapper.
- **RetroAchievements:** an account API key can query recent games, user summaries, game progress and achievements ([RetroAchievements API](https://api-docs.retroachievements.org/), [recently played endpoint](https://api-docs.retroachievements.org/v1/get-user-recently-played-games.html)). This is valuable for retro/emulator activity and achievement enrichment, but it does not provide exact historical session intervals.
- **Battle.net:** Blizzard's public developer APIs are useful for specific supported game profiles and game data, not for a complete Battle.net launcher library/session feed. Blizzard describes OAuth as a way for fan sites to access selected game data ([Blizzard developer API announcement](https://worldofwarcraft.blizzard.com/en-us/news/15336025/attention-web-developers)). Use it only when Playdex wants a game-specific feature; use Playnite for launcher-wide sessions.
- **Bungie/Destiny and similar game APIs:** these can provide deep title-specific activity. Bungie's official API has OAuth and profile/activity surfaces ([Bungie API](https://bungie-net.github.io/)). They are worthwhile only as optional game integrations, not as replacements for a platform provider.

## Fit with the current Playdex codebase

The core already has most of the right shape:

- `PlayniteSessionRequest` carries `GameTitle`, `GameId`, `GameProvider` and optional duration, and the Playnite module supports complete sessions plus start/ping/end flows (`src/Api/Modules/Playnite`).
- The Windows wrapper's ping payload is provider-neutral: title, ID and provider. Most wrapper/session logic is not Steam-specific. The Steam-specific pieces are the default provider, Steam ID discovery/validation, Ludusavi lookup, and server-side Steam cover URL generation.
- `PlayniteRepository.UpsertGame` safely falls back to the `unknown` platform when a provider key is not seeded. However, the current migration seeds only Steam, Playnite, PSN, manual and hardware families. Epic/GOG/Xbox-PC/etc. would therefore lose useful platform attribution until provider aliases are added.
- The schema currently records the **credential/source** on a session and the **platform** on a game. For a Playnite umbrella credential this is workable, but imported/manual data should also carry provenance and precision rather than pretending every interval is exact.

Recommended model/API changes before broad ingestion:

1. Seed stable provider/platform keys (`epic`, `gog`, `xbox-pc`, `ubisoft`, `ea`, `battlenet`, `amazon-games`, `itch`, `retroachievements`, `switch`). Maintain an alias map for Playnite source names.
2. Add session provenance such as `source_kind` (`live-event`, `provider-poll`, `manual-text`, `screenshot-ocr`, `aggregate-import`), `external_event_id`, `precision`, `raw_payload`, and optional confidence. Enforce idempotency where an external ID exists.
3. Keep cumulative provider snapshots separate from exact `play_sessions`, or add an explicitly aggregate/approximate record type. Never manufacture sessions from all-time playtime.
4. Generalize the current Playnite-named endpoint/credential to a local-agent contract over time. Preserve the existing route for compatibility, but let Playnite, a process watcher, and future launchers send the same event envelope.
5. Implement one provider-neutral “propose session(s) → validate/resolve → confirm → commit” service, then expose it through web upload, chat/Telegram and optionally MCP.

## Suggested implementation order

1. **Broaden the Windows wrapper/Playnite path** and add provider aliases. This immediately covers the largest number of PC platforms with exact sessions.
2. **Build structured manual/chat/OCR ingestion** with confirmation and provenance. This closes Nintendo, offline-console and provider-outage gaps.
3. **Add itch.io ownership and RetroAchievements activity** as small, supported integrations if library/achievement features are in scope.
4. **Spike official Xbox OAuth + title history** and measure completeness against a real account. Add live presence only if the auth/production constraints are acceptable. Evaluate OpenXBL separately as an explicitly unofficial beta.
5. **Defer direct Epic/GOG/EA/Ubisoft/Amazon server pollers.** They offer less reliability and less exact session data than the local wrapper, while substantially increasing authentication and maintenance cost.

## Implemented OpenXBL proof of concept

Playdex now includes an experimental `xbox` polling provider backed by OpenXBL's documented `GET /v2/presence` and `GET /v2/presence/{xuid}` endpoints.

To try it:

1. Create an OpenXBL API key at [xbl.io/dashboard](https://xbl.io/dashboard).
2. Open Playdex **Settings** and find **Xbox presence experiment**.
3. Enter the API key. Leave XUID empty to query the Xbox account linked to the key, or provide a numeric XUID to use OpenXBL's XUID endpoint.
4. Keep the provider active. The normal polling lifecycle will create and end sessions from active full-screen title transitions.

The client sends the key in OpenXBL's `X-Authorization` header, preserves raw presence payloads in `poll_results`, honors HTTP 429 backoff, and ignores offline, inactive, and explicitly background titles. The production default of one poll per minute consumes about 60 requests per hour for one credential, within the free tier advertised during this research. This remains an unofficial, forward-only signal: privacy settings, stale presence, upstream changes, or OpenXBL availability may create gaps.
