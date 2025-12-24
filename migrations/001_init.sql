create table if not exists accounts (
  id bigserial primary key,
  display_name text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists platform_accounts (
  id bigserial primary key,
  account_id bigint not null references accounts(id) on delete cascade,
  platform text not null,
  external_id text not null,
  display_name text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (platform, external_id)
);

create table if not exists games (
  id bigserial primary key,
  platform text not null,
  title text not null,
  external_id text,
  canonical_key text not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (canonical_key)
);

create table if not exists play_sessions (
  id bigserial primary key,
  platform_account_id bigint not null references platform_accounts(id) on delete cascade,
  game_id bigint not null references games(id) on delete cascade,
  started_at timestamptz not null,
  ended_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
