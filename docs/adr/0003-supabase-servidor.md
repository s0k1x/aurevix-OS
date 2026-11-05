# ADR 0003 — Supabase como servidor de versiones

**Estado:** aceptada

## Decisión
- **Storage público** `releases` para `manifests/{canal}/manifest.json(.sig)`.
- **Tabla `releases`** (con RLS) para que la web liste las versiones publicadas. Lectura pública solo de filas `published = true`; **ninguna** política de escritura: solo `service_role` desde GitHub Actions.
- Los instaladores se alojan en **GitHub Releases** (el repositorio es público; ahorra almacenamiento).

## Razones
Sin servidor propio que mantener, con RLS y claves separadas (publishable para lectura, service_role solo en CI).

## Consecuencias
- La clave `service_role` nunca debe aparecer en el repositorio, la web ni el cliente.
- Si el proyecto de Supabase se pausa (plan gratuito) las actualizaciones dejan de comprobarse hasta reactivarlo; el cliente falla de forma silenciosa y sigue funcionando.
- No se registran dispositivos ni se recopila telemetría en esta versión.
