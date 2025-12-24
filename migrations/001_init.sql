create table if not exists sample_checks (
  id bigserial primary key,
  created_at timestamptz not null default now()
);
