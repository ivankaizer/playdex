create table if not exists users (
  id bigserial primary key,
  email text not null unique,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists platform_credentials (
  id bigserial primary key,
  user_id bigint not null,
  platform text not null,
  api_key text,
  account_id text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

alter table platform_credentials
  add constraint platform_credentials_user_api_account_key
  unique (user_id, api_key, account_id);

alter table platform_credentials
  add constraint platform_credentials_user_id_fkey
  foreign key (user_id) references users(id) on delete cascade;

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
  platform_credential_id bigint not null references platform_credentials(id) on delete cascade,
  game_id bigint not null references games(id) on delete cascade,
  started_at timestamptz not null,
  ended_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
