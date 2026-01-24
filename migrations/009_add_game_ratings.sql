create table if not exists game_ratings (
  id bigserial primary key,
  user_id bigint not null references users(id) on delete cascade,
  game_id bigint not null references games(id) on delete cascade,
  rating smallint not null check (rating >= 1 and rating <= 5),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (user_id, game_id)
);

create index if not exists game_ratings_user_id_idx
  on game_ratings (user_id);

create index if not exists game_ratings_game_id_idx
  on game_ratings (game_id);
