# Cómo publicar una versión

## Preparación (una sola vez)

### 1. Clave de firma como secreto de GitHub
La clave privada se generó con `aurevix-release keygen`. **No está en el repositorio.** Guárdala en un gestor de contraseñas y cópiala como secreto:

GitHub → repositorio → *Settings → Secrets and variables → Actions → New repository secret*

| Secreto | Valor |
|---|---|
| `AUREVIX_SIGNING_KEY` | Contenido completo de `aurevix-2026-01.private.pem` (incluidas las líneas `-----BEGIN…`) |
| `SUPABASE_SERVICE_ROLE_KEY` | Clave `service_role` de Supabase (*Project Settings → API Keys*). **Nunca** la pongas en el código ni en la web |

Si pierdes la clave privada tendrás que generar otra y publicar una versión del cliente que incluya la nueva pública (ver rotación en [UPDATE_MANIFEST.md](UPDATE_MANIFEST.md)).

### 2. GitHub Pages para la web
*Settings → Pages → Build and deployment → Source: **GitHub Actions***. La web se publica al cambiar `web/` en `main`.

## Publicar

1. Edita `RELEASE_NOTES.md` con las novedades.
2. Fusiona los cambios en `main`.
3. Crea y sube la etiqueta:
   ```bash
   git tag v0.2.0
   git push origin v0.2.0
   ```
   Canales: `v1.2.3` → stable · `v1.2.3-beta.1` → beta · `v1.2.3-dev.1` → developer.

El workflow `Release`:
1. Ejecuta las pruebas.
2. Publica el Control Center (win-x64, autocontenido).
3. Crea el **Launcher Installer** (Inno Setup) y el paquete del **ISO Builder**.
4. Genera el manifiesto, lo **firma** y comprueba que la firma verifica con la clave pública incrustada.
5. Crea la *GitHub Release* con ambos ficheros.
6. Sube el manifiesto y la firma a Supabase Storage y registra la versión en la tabla `releases`.

Resultado: los equipos con AUREVIX verán la pantalla de actualización en su próxima comprobación (al iniciar sesión o cada 4 h) y la web mostrará la nueva versión.

## Despliegue gradual

Para ir poco a poco, genera el manifiesto con `--rollout 10` (10 % de los equipos) y vuelve a publicar con `100` más tarde (aumentando `--sequence`).

## Herramienta de firma (manual)

```bash
dotnet run --project tools/Aurevix.ReleaseTool -- keygen   --out ./keys --key-id aurevix-2026-02
dotnet run --project tools/Aurevix.ReleaseTool -- manifest --channel stable --version 0.2.0 --sequence 2 \
    --installer AurevixSetup-0.2.0.exe --installer-url https://… --notes RELEASE_NOTES.md --out manifest.json
dotnet run --project tools/Aurevix.ReleaseTool -- sign     --manifest manifest.json --key ./keys/aurevix-2026-01.private.pem --key-id aurevix-2026-01
dotnet run --project tools/Aurevix.ReleaseTool -- verify   --manifest manifest.json --public src/Aurevix.Update/Keys/aurevix-2026-01.public.pem --key-id aurevix-2026-01
```

## Firma de código (pendiente)

El instalador `.exe` **no está firmado con un certificado de Authenticode**, por lo que Windows SmartScreen puede mostrar un aviso de "editor desconocido". Un certificado de firma de código se compra a una autoridad certificadora y no puedo obtenerlo por ti. Mientras tanto, la integridad se garantiza con el SHA-256 y la firma del manifiesto.
