-- Playdex Contract Test Seed Data
-- This script is embedded and executed by ContractFixture.
-- All IDs are set explicitly so both APIs return deterministic identifiers.

-- ── Clean slate (order respects FK constraints) ──────────────────────────
truncate table
  play_sessions,
  poll_results,
  game_connections,
  game_ratings,
  manual_game_inputs,
  ignored_games,
  platform_credentials,
  user_settings,
  games,
  users,
  platforms
cascade;

-- ── Platforms ─────────────────────────────────────────────────────────────
insert into platforms (id, key, name, igdb_platform_id, parent_id)
values
  (100, 'pc',   'PC',   6,   null),
  (101, 'steam', 'Steam', null, 100),
  (102, 'ps5',  'PS5',  167, null);
select setval('platforms_id_seq', 102);

-- ── Users ─────────────────────────────────────────────────────────────────
-- The password column is left null; ContractFixture sets it to a known
-- bcrypt hash after seeding so both APIs can verify the same credential.
insert into users (id, email, password, is_admin)
values (100, 'contract-test@playdex.app', null, true);
select setval('users_id_seq', 100);

-- ── Games ─────────────────────────────────────────────────────────────────
insert into games (id, title, external_id, canonical_key, platform_id)
values
  (100, 'Test Game Alpha', 'ext-alpha',   'test-game-alpha', 101),
  (101, 'Test Game Beta',  'ext-beta',    'test-game-beta',  101),
  (102, 'Test Game Gamma', 'ext-gamma',   'test-game-gamma', 102);
select setval('games_id_seq', 102);

-- ── Platform Credentials ──────────────────────────────────────────────────
insert into platform_credentials (id, user_id, api_key, account_id, active, platform_id)
values (100, 100, 'steam-api-key-contract-test', 'contract-steam-account', true, 101);
select setval('platform_credentials_id_seq', 100);

-- ── Play Sessions ─────────────────────────────────────────────────────────
insert into play_sessions (id, platform_credential_id, game_id, started_at, ended_at)
values
  (100, 100, 100, '2026-01-15 10:00:00+00', '2026-01-15 12:30:00+00'),
  (101, 100, 101, '2026-01-16 14:00:00+00', '2026-01-16 16:45:00+00'),
  (102, 100, 100, '2026-01-17 09:00:00+00', null);
select setval('play_sessions_id_seq', 102);
