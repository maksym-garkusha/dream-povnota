-- Історія карток: тільки зміни. Версія картки живе в діапазоні знімків
-- valid_during = [перший знімок, знімок, з якого вона вже не така); верхня межа
-- відкрита, поки версія поточна. Тимчасовий первинний ключ PostgreSQL 18
-- (WITHOUT OVERLAPS) гарантує, що в однієї картки версії не перекриваються.
create extension if not exists btree_gist;

create table rules (                       -- один рядок на кожен окремий перелік перевірок
  id      smallint generated always as identity primary key,
  hash    bytea not null unique,           -- sha256 переліку кодів перевірок
  checks  jsonb not null
);

create table snapshot (                    -- один рядок на кожен імпортований випуск data.json
  id            int generated always as identity primary key,
  generated     timestamptz not null unique,
  synced        timestamptz,
  imported_at   timestamptz not null default now(),
  content_hash  bytea not null,
  rules_id      smallint not null references rules,
  rules_changed boolean not null,          -- перелік перевірок інший, ніж у попередньому знімку
  open_cards    int not null,
  added int not null default 0, removed int not null default 0, changed int not null default 0
);

create table organisation (
  id      int generated always as identity primary key,
  key     text not null unique check (length(key) between 1 and 400),  -- ЄДРПОУ або 'n:' і назва
  edrpou  char(8) check (edrpou ~ '^[0-9]{8}$'),
  title   text not null,
  first_snapshot int not null references snapshot,
  last_snapshot  int not null references snapshot
);

create table card (
  id     int generated always as identity primary key,
  code   text not null unique check (code ~ '^[0-9]{6}-[0-9A-F]{8}$'),
  title  text not null,
  first_snapshot int not null references snapshot,
  last_snapshot  int not null references snapshot
);

create table card_version (
  card_id      int not null references card,
  valid_during int4range not null check (not isempty(valid_during) and not lower_inf(valid_during)),
  org_id       int not null references organisation,
  region       text not null check (region ~ '^([0-9]{2}|multi|none)$'),
  stage        text not null,
  sector       text not null,
  updated      date not null,
  applicable   int not null,
  filled       int not null check ((filled & ~applicable) = 0),
  score_applicable smallint not null,     -- доречних перевірок, що входять у наповненість
  score_filled     smallint not null,
  seeks_funding    boolean not null,
  primary key (card_id, valid_during without overlaps)
);
create index card_version_org    on card_version (org_id);
create index card_version_region on card_version (region);
create index card_version_from   on card_version (lower(valid_during));
create index card_version_to     on card_version (upper(valid_during)) where not upper_inf(valid_during);
create index card_version_current on card_version (card_id) where upper_inf(valid_during);

create table slice_stat (                 -- розрізи all/stages/regions/sectors з data.json
  snapshot_id int not null references snapshot,
  dimension   char(1) not null check (dimension in ('a','g','r','s')),
  key         text not null,
  title       text not null,
  open_cards  int not null,
  complete_cards int not null,
  avg_score   double precision not null,
  checks      int[] not null,             -- доречно, заповнено, доречно, заповнено... у порядку rules
  primary key (snapshot_id, dimension, key)
);
