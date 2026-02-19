create table if not exists platforms (
  id bigserial primary key,
  key text not null unique,
  name text not null,
  igdb_platform_id integer,
  parent_id bigint references platforms(id) on delete set null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

insert into platforms (key, name, igdb_platform_id)
values
  ('ps5', 'PS5', 167),
  ('xbox-series-xs', 'Xbox Series X|S', 169),
  ('pc', 'PC', 6),
  ('switch-1', 'Nintendo Switch 1', 130),
  ('switch-2', 'Nintendo Switch 2', 508),
  ('unknown', 'Unknown', null);

insert into platforms (key, name, parent_id)
values
  ('steam', 'Steam', (select id from platforms where key = 'pc')),
  ('playnite', 'Playnite', (select id from platforms where key = 'pc')),
  ('psn', 'PSN', (select id from platforms where key = 'ps5')),
  ('manual', 'Manual', null);

alter table platform_credentials
  add column if not exists platform_id bigint;

alter table games
  add column if not exists platform_id bigint;

update platform_credentials pc
set platform_id = p.id
from platforms p
where pc.platform_id is null
  and p.key = pc.platform;

update platform_credentials
set platform_id = (select id from platforms where key = 'unknown')
where platform_id is null;

update games g
set platform_id = p.id
from platforms p
where g.platform_id is null
  and p.key = g.platform;

update games
set platform_id = (select id from platforms where key = 'ps5')
where platform_id is null
  and lower(platform) in ('ps5', 'playstation 5', 'playstation5', 'ps 5');

update games
set platform_id = (select id from platforms where key = 'xbox-series-xs')
where platform_id is null
  and lower(platform) in (
    'xbox series x|s',
    'xbox series x',
    'xbox series s',
    'xbox series xs',
    'xbox series'
  );

update games
set platform_id = (select id from platforms where key = 'pc')
where platform_id is null
  and lower(platform) in ('pc', 'windows');

update games
set platform_id = (select id from platforms where key = 'switch-1')
where platform_id is null
  and lower(platform) in (
    'nintendo switch',
    'switch',
    'switch 1',
    'nintendo switch 1'
  );

update games
set platform_id = (select id from platforms where key = 'switch-2')
where platform_id is null
  and lower(platform) in ('switch 2', 'nintendo switch 2');

update games
set platform_id = (select id from platforms where key = 'steam')
where platform_id is null
  and lower(platform) = 'steam';

update games
set platform_id = (select id from platforms where key = 'playnite')
where platform_id is null
  and lower(platform) = 'playnite';

update games
set platform_id = (select id from platforms where key = 'ps5')
where platform_id is null
  and lower(platform) = 'psn';

update games
set platform_id = (select id from platforms where key = 'manual')
where platform_id is null
  and lower(platform) = 'manual';

update games
set platform_id = (select id from platforms where key = 'unknown')
where platform_id is null
  and lower(platform) = 'unknown';

update games
set platform_id = (select id from platforms where key = 'unknown')
where platform_id is null
  and lower(platform) = 'igdb';

update games
set platform_id = (select id from platforms where key = 'unknown')
where platform_id is null;

alter table platform_credentials
  alter column platform_id set not null;

alter table games
  alter column platform_id set not null;

alter table platform_credentials
  add constraint platform_credentials_platform_id_fkey
  foreign key (platform_id) references platforms(id);

alter table games
  add constraint games_platform_id_fkey
  foreign key (platform_id) references platforms(id);

alter table platform_credentials
  drop column platform;

alter table games
  drop column platform;
