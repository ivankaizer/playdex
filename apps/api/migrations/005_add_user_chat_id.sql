alter table users
  add column if not exists chat_id text;

create unique index if not exists users_chat_id_unique
  on users (chat_id)
  where chat_id is not null;
