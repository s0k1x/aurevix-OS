-- AUREVIX OS · Servidor de versiones
-- releases: catálogo público de versiones (lo lee la web de descargas).
-- Storage "releases": manifiestos firmados por canal (los lee AUREVIX Update).
-- Solo el rol service_role (GitHub Actions) puede escribir. La web y los clientes solo leen.

create table if not exists public.releases (
    id                 uuid primary key default gen_random_uuid(),
    version            text not null check (version ~ '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$'),
    channel            text not null default 'stable' check (channel in ('stable', 'beta', 'developer')),
    title              text not null,
    notes              text not null default '',
    published          boolean not null default false,
    released_at        timestamptz not null default now(),
    min_supported_version text,
    manifest_sequence  bigint not null,
    rollout_percent    integer not null default 100 check (rollout_percent between 0 and 100),
    installer_url      text not null check (installer_url like 'https://%'),
    installer_sha256   text not null check (installer_sha256 ~ '^[0-9a-f]{64}$'),
    installer_size     bigint not null check (installer_size > 0),
    isobuilder_url     text check (isobuilder_url like 'https://%'),
    isobuilder_sha256  text check (isobuilder_sha256 ~ '^[0-9a-f]{64}$'),
    created_at         timestamptz not null default now(),
    unique (channel, version)
);

create index if not exists releases_channel_published_idx
    on public.releases (channel, published, released_at desc);

comment on table public.releases is 'Versiones de AUREVIX OS publicadas. Lectura pública solo de filas publicadas.';

alter table public.releases enable row level security;

drop policy if exists "Lectura pública de versiones publicadas" on public.releases;
create policy "Lectura pública de versiones publicadas"
    on public.releases for select
    to anon, authenticated
    using (published = true);

-- Sin políticas de insert/update/delete: solo service_role (que ignora RLS) puede escribir.
revoke insert, update, delete on public.releases from anon, authenticated;

-- Bucket público de solo lectura para manifiestos firmados.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('releases', 'releases', true, 1048576, array['application/json', 'application/octet-stream', 'text/plain'])
on conflict (id) do update
    set public = excluded.public,
        file_size_limit = excluded.file_size_limit,
        allowed_mime_types = excluded.allowed_mime_types;
