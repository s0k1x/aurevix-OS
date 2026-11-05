-- AUREVIX Installer (un único .exe con las tres opciones: instalar aquí, reinstalar limpio, crear USB/ISO).
alter table public.releases
  add column if not exists launcher_url text check (launcher_url like 'https://%'),
  add column if not exists launcher_sha256 text check (launcher_sha256 ~ '^[0-9a-f]{64}$'),
  add column if not exists launcher_size bigint check (launcher_size > 0);
