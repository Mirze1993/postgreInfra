create table if not exists sensor_data (
   id     serial primary key,
   sensor text not null,
   value  numeric,
   ts     timestamptz default now()
);