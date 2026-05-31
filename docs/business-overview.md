# Playdex — Business Overview

Playdex is a **multi-platform playtime tracker** that automatically records what you play, when, and for how long — across Steam, PlayStation, and PC (via Playnite).

---

## 1. How It Works (High-Level)

```mermaid
flowchart TB
    subgraph External["External Services"]
        Steam["Steam Web API"]
        PSN["PSN API"]
        IGDB["IGDB API"]
        Playnite["Playnite (PC)"]
    end

    subgraph Playdex["Playdex System"]
        Poller["Polling Engine"]
        API["API Server"]
        DB[("PostgreSQL")]
        ImageGen["Notification Image Generator"]
        Telegram["Telegram Bot"]
    end

    subgraph User["User"]
        Browser["Web Dashboard"]
        TG["Telegram Chat"]
        Game["Playing Games"]
    end

    External --> Playdex
    Poller --> Game
    Poller --> Steam
    Poller --> PSN
    Playnite --> API
    API --> DB
    API --> ImageGen
    ImageGen --> Telegram
    Telegram --> TG
    API --> Browser
    API --> IGDB
```

The system periodically polls Steam and PSN to detect what the user is playing, accepts session data from Playnite, and stores everything in a PostgreSQL database. Users view their play history on a web dashboard or receive Telegram notifications.

---

## 2. Play Session Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Idle: User stops playing

    Idle --> Playing: Poll detects new game\nor Playnite starts session\nor manual start
    Playing --> Playing: Same game (polls keep\nsession alive)
    Playing --> Idle: Poll detects no game\nor Playnite ends session\nor manual stop

    state Idle {
        [*] --> NoSession
    }

    state Playing {
        [*] --> SessionActive
        SessionActive --> SessionExtended: Activity confirmed
        SessionExtended --> SessionActive: Next poll cycle
    }
```

### Session Sources

| Source | How It Works | Granularity |
|---|---|---|
| **Steam** | Polls `GetPlayerSummaries` every ~60s to get the currently played game | Auto, ~1 min |
| **PSN** | Polls PSN presence API to detect the current game | Auto, ~1 min |
| **Playnite** | PC app sends start/ping/end HTTP requests | Manual (per-session) |
| **Manual** | User starts/stops sessions via the web UI | Manual |

---

## 3. Multi-Platform Game Identity

```mermaid
flowchart LR
    subgraph UserPerspective["User Sees"]
        Hades["Hades II"]
    end

    subgraph SteamInstance["Steam"]
        SGame["Steam App 1145350"]
        PC["Platform: PC"]
    end

    subgraph PSNInstance["PSN"]
        PGame["PSN Title EP4040-..." ]
        PS5["Platform: PS5"]
    end

    Hades -->|Game Connection| SteamInstance
    Hades -->|Game Connection| PSNInstance

    SGame -.->|IGDB match| IGDB
    PGame -.->|IGDB match| IGDB
```

The same game across platforms is linked via **game_connections** (user-level), and enriched with metadata from IGDB (covers, titles). This enables unified stats (e.g., "47 hours of Hades II" combining Steam + PS5 time).

---

## 4. Automated Polling Flow

```mermaid
sequenceDiagram
    participant Poller as Polling Engine
    participant DB as Database
    participant Steam as Steam API
    participant PSN as PSN API
    participant User as User Dashboard
    participant Telegram as Telegram Bot

    loop Every ~60 seconds
        Poller->>DB: Load active credentials
        Poller->>Steam: Poll Steam users
        Poller->>PSN: Poll PSN users

        alt Game changed or started
            Poller->>DB: End previous session
            Poller->>DB: Start new session
            Poller->>DB: Record poll result
            Poller->>Telegram: Send notification image
            Telegram->>User: "You started playing X"
        else Same game
            Poller->>DB: Extend session (update ended_at)
            Poller->>DB: Record poll result
        else No game detected
            Poller->>DB: End session if active
            Poller->>DB: Record poll result
        end

        User->>DB: View updated stats (dashboard)
    end
```

---

## 5. Data Model (Core Business Entities)

```mermaid
erDiagram
    User ||--o{ PlatformCredential : has
    User ||--o{ GameRating : rates
    User ||--o{ IgnoredGame : ignores
    User ||--o{ GameConnection : links
    User ||--o{ ManualGameInput : submits
    User ||--o{ UserSetting : configures

    Platform ||--o{ PlatformCredential : authenticates
    Platform ||--o{ Game : catalogs

    PlatformCredential ||--o{ PlaySession : records
    PlatformCredential ||--o{ PollResult : tracks

    Game ||--o{ PlaySession : has
    Game ||--o{ PollResult : maybe
    Game ||--o{ GameRating : rated
    Game ||--o{ IgnoredGame : skipped
    Game ||--o{ GameConnection : linked
    Game ||--o{ ManualGameInput : mapped

    Platform {
        bigint id
        string key
        string name
        bigint igdb_platform_id
        bigint parent_id
    }

    Game {
        bigint id
        string title
        string external_id
        string canonical_key
        string capsule_url
        bigint platform_id
    }

    PlaySession {
        bigint id
        bigint platform_credential_id
        bigint game_id
        datetime started_at
        datetime ended_at
    }
```

The business entities center on **users** who own **platform credentials**. Each credential produces **play sessions** tied to **games** on specific **platforms**. Cross-platform identity is handled by **game connections** per user.

---

## 6. User Value Stream

```mermaid
flowchart LR
    A["🎮 Play Games"] --> B["⏱ Auto-Detect\n(Steam/PSN/Playnite)"]
    B --> C["💾 Store Sessions\nin Database"]
    C --> D["📊 View Dashboard\n& Statistics"]
    C --> E["📬 Get Telegram\nNotifications"]
    C --> F["📈 Yearly Recap\n& Insights"]
    D --> G["🔗 Link Games\nAcross Platforms"]
    F --> G
    G --> D
```

The core value: **play once, track everywhere** — no manual logging, unified cross-platform history, and rich yearly retrospectives.

---

## 7. Tech Stack Summary

| Layer | Technology |
|---|---|
| Runtime | Bun |
| API Framework | Elysia |
| Database | PostgreSQL 16 (Kysely ORM) |
| Frontend | React + Vite + Tailwind CSS |
| Auth | JWT + bcrypt |
| Notifications | Satori → Resvg → Sharp → Telegram |
| Infrastructure | Docker, GitHub Actions → AWS ECR |
