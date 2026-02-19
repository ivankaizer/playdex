create table if not exists user_settings (
  user_id bigint primary key references users(id) on delete cascade,
  ignore_short_sessions_minutes int not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
