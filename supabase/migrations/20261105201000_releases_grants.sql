-- Permisos explícitos de la tabla releases (aplicado el 2026-11-05).
-- El proyecto no concede privilegios por defecto a las tablas nuevas, y los que
-- traía (TRUNCATE para anon/authenticated) permitían vaciar la tabla saltándose RLS.
revoke all on public.releases from anon, authenticated;
grant select on public.releases to anon, authenticated;          -- RLS limita a published = true
revoke truncate, references, trigger on public.releases from service_role;
grant select, insert, update on public.releases to service_role; -- solo GitHub Actions
