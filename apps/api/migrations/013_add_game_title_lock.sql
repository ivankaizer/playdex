alter table games
  add column if not exists title_locked boolean not null default false;
