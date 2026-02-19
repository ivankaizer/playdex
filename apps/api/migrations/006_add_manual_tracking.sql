create table if not exists manual_game_inputs (
  id bigserial primary key,
  user_id bigint not null references users(id) on delete cascade,
  raw_title text not null,
  canonical_title text not null,
  mapped_game_id bigint references games(id) on delete set null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create unique index if not exists manual_game_inputs_user_title_idx
  on manual_game_inputs (user_id, canonical_title);

create index if not exists manual_game_inputs_mapped_game_id_idx
  on manual_game_inputs (mapped_game_id);
