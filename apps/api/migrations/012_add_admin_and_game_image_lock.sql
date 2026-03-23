alter table users
  add column if not exists is_admin boolean not null default false;

alter table games
  add column if not exists capsule_url_locked boolean not null default false;
