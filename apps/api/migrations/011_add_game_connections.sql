create table if not exists game_connections (
  user_id bigint not null references users(id) on delete cascade,
  game_id bigint not null references games(id) on delete cascade,
  connected_game_id bigint not null references games(id) on delete cascade,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  primary key (user_id, game_id, connected_game_id),
  check (game_id < connected_game_id)
);

create index if not exists game_connections_user_id_idx
  on game_connections (user_id);
