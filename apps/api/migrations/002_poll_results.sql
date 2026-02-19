create table if not exists poll_results (
  id bigserial primary key,
  platform_credential_id bigint not null references platform_credentials(id) on delete cascade,
  game_id bigint references games(id) on delete set null,
  polled_at timestamptz not null,
  raw_payload jsonb,
  created_at timestamptz not null default now()
);

create index if not exists poll_results_platform_credential_id_idx
  on poll_results (platform_credential_id);

create index if not exists poll_results_polled_at_idx
  on poll_results (polled_at);
