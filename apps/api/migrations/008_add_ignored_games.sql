create table if not exists ignored_games (
  id bigserial primary key,
  user_id bigint not null references users(id) on delete cascade,
  game_id bigint not null references games(id) on delete cascade,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (user_id, game_id)
);

create index if not exists ignored_games_user_id_idx
  on ignored_games (user_id);
