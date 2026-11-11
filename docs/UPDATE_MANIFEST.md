# Manifiesto de actualizaciones

## Ubicación

```
https://zttqiphwurnferzlubzy.supabase.co/storage/v1/object/public/releases/manifests/{canal}/manifest.json
https://zttqiphwurnferzlubzy.supabase.co/storage/v1/object/public/releases/manifests/{canal}/manifest.json.sig
```

`{canal}` = `stable` | `beta` | `developer`. La URL base se puede cambiar con `%ProgramData%\Aurevix\update.json` (la firma sigue siendo obligatoria).

## manifest.json

```json
{
  "schemaVersion": 1,
  "product": "aurevix-os",
  "channel": "stable",
  "sequence": 1793900000,
  "publishedAt": "2026-11-05T20:00:00+00:00",
  "expiresAt": "2026-12-05T20:00:00+00:00",
  "latest": {
    "version": "0.2.0",
    "minimumVersion": "0.1.0",
    "title": "AUREVIX OS 0.2.0",
    "notes": "Texto de novedades",
    "requiresRestart": false,
    "rolloutPercent": 100,
    "packages": [
      {
        "id": "aurevix-app",
        "kind": "appInstaller",
        "url": "https://github.com/s0k1x/aurevix-OS/releases/download/v0.2.0/AurevixSetup-0.2.0.exe",
        "sha256": "<64 hex>",
        "size": 160000000
      }
    ]
  }
}
```

`kind`: `appInstaller` (instalador de la app), `themePack` (zip de recursos, sin ejecutables) y `tweakCatalog` (JSON de datos). En esta versión solo se instala `appInstaller`; los otros tipos están reservados para módulos futuros.

## Firma

`manifest.json.sig`:

```json
{ "keyId": "aurevix-2026-01", "algorithm": "ECDSA-P256-SHA256", "signature": "<base64>" }
```

La firma es sobre los **bytes exactos** de `manifest.json`. Las claves públicas de confianza van **incrustadas en el cliente** (`src/Aurevix.Update/Keys/*.public.pem`, el nombre del fichero es el `keyId`). Un manifiesto no firmado, con otra clave o manipulado se ignora.

### Rotación de claves

1. Generar un par nuevo (`aurevix-release keygen`).
2. Publicar una versión del cliente que incluya **también** la clave pública nueva.
3. Cuando la mayoría haya actualizado, empezar a firmar con la nueva y, más adelante, retirar la antigua del cliente.

## Reglas de validación del cliente

| Comprobación | Resultado si falla |
|---|---|
| Firma válida con una clave incrustada | Se ignora (error) |
| `product`, `schemaVersion` y `channel` correctos | Se ignora |
| `expiresAt` no vencido | Se ignora (evita manifiestos "congelados") |
| `sequence` ≥ la última vista | Se ignora (anti-downgrade / replay) |
| `latest.version` > versión instalada | Si no, "al día" (nunca se ofrece una versión menor) |
| Cada paquete: HTTPS, host permitido, SHA-256 válido, tamaño razonable | Se ignora |
| `minimumVersion` ≤ versión instalada | "Requiere actualización manual" desde la web |
| Despliegue gradual: `hash(installId:version) % 100 < rolloutPercent` | "Llegará pronto" |

Al descargar, además se comprueba que el tamaño recibido coincide y que el SHA-256 del fichero coincide; si no, se borra.

## Qué NO hace el sistema

- No ejecuta comandos ni scripts recibidos del servidor. El instalador se lanza con argumentos **fijos** definidos en el cliente.
- No recibe conexiones entrantes: el cliente solo hace peticiones salientes HTTPS.
- No controla el PC de forma remota.

## Actualizaciones de Windows

Son independientes. Las actualizaciones profundas de Windows (características, versiones) siguen su ciclo con Windows Update y pueden requerir reinicio; AUREVIX no las sustituye ni promete que un cambio de Windows se pueda instalar como paquete pequeño.

## Estado actual

Implementados: formato, firma, validación, rollout, descarga verificada, historial, rollback manual reinstalando el instalador anterior. **No implementados**: instalación transaccional automática con rollback sin intervención, autenticación del dispositivo y paquetes `themePack` / `tweakCatalog` (ver [BACKLOG.md](BACKLOG.md)).
