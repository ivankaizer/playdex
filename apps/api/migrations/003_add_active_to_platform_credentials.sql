alter table platform_credentials
  add column if not exists active boolean not null default true;
