# AUREVIX OS

Experiencia de **Windows 11** personalizada, modular y actualizable. AUREVIX **no** es un sistema operativo nuevo ni redistribuye Windows: usa la imagen oficial y con licencia de Microsoft y añade, de forma transparente y reversible, un asistente de primer inicio, un Control Center permanente, perfiles de rendimiento y un sistema de actualizaciones firmadas.

> Estado: **versión de laboratorio 0.1.0**. Pruébala siempre primero en una máquina virtual.

## Qué incluye

| Componente | Estado |
|---|---|
| **Control Center** (WPF, .NET 8) con 9 secciones: Inicio, Diagnóstico, Rendimiento, Perfiles, Optimización, Apariencia, Actualizaciones, Recuperación, Ajustes | Escrito. Pendiente de compilar y probar en Windows |
| **AUREVIX Setup** (asistente de primer inicio): diagnóstico, informe, punto de restauración, perfil, resumen | Escrito. Pendiente de probar |
| **Diagnóstico** de solo lectura (WMI) con niveles Correcto / Revisar / Requiere atención / No disponible. Nunca inventa temperaturas | Lógica probada; recopilación pendiente de probar en Windows |
| **Catálogo de ajustes reversibles** (10 ajustes, solo HKCU) y 4 perfiles, con copia de seguridad y reversión automática si falla | Probado (36 pruebas automáticas) |
| **AUREVIX Update**: manifiesto firmado ECDSA P-256, anti-downgrade, caducidad, despliegue gradual, descarga verificada con SHA-256, canales Stable/Beta/Developer | Probado |
| **Pantalla automática de actualización** al publicar una versión nueva | Escrita. Pendiente de probar |
| **Launcher Installer** (Inno Setup) | Escrito. Se compila en GitHub Actions |
| **ISO Builder** (convierte tu ISO oficial en una ISO de AUREVIX) | Escrito. Pendiente de probar |
| **Servidor de versiones** (Supabase: tabla `releases` + Storage `releases`) | Desplegado |
| **Web de descargas** (`web/`) | Lista; se publica con GitHub Pages |
| **AUREVIX Core Service** (servicio con privilegios mínimos) | **No implementado** (Hito 4). Hoy las acciones elevadas piden UAC puntualmente |
| Personalización profunda del shell (barra de tareas, Inicio, Explorador, iconos, cursores) | **No incluida** a propósito. Ver ADR 0005 |
| Temperaturas y uso de GPU | **No incluidos**: no hay una fuente fiable sin privilegios. Ver ADR 0006 |

## Instalar

- **Launcher Installer** (Windows 11 ya instalado): descarga `AurevixSetup-x.y.z.exe` de la web o de [Releases](../../releases) y ejecútalo.
- **ISO propia**: descarga la ISO *oficial* de Windows 11 desde Microsoft y usa el **ISO Builder** (`iso-builder/`). Ver [docs/ISO_BUILDER.md](docs/ISO_BUILDER.md).

> No se publica ninguna ISO de Windows modificada: la licencia no lo permite. Ver [ADR 0004](docs/adr/0004-iso-y-licencias.md).

## Documentación

- [Arquitectura](docs/ARCHITECTURE.md)
- [Manifiesto de actualizaciones](docs/UPDATE_MANIFEST.md)
- [Cómo publicar una versión](docs/RELEASING.md)
- [Probar en una máquina virtual](docs/VM_TESTING.md)
- [Construir la ISO](docs/ISO_BUILDER.md)
- [Backlog por hitos](docs/BACKLOG.md)
- [Decisiones de arquitectura (ADR)](docs/adr)

## Desarrollo

```bash
dotnet test tests/Aurevix.Tests          # funciona en Linux, macOS y Windows
dotnet build src/Aurevix.ControlCenter   # requiere Windows
```

Estructura: `src/` (módulos), `tests/`, `tools/` (herramienta de firma), `installer/`, `iso-builder/`, `supabase/migrations/`, `web/`, `docs/`.

## Principios

Seguridad y reversibilidad antes que rendimiento. No se desactivan Defender, Firewall, Windows Update, SmartScreen ni UAC. No hay claves, activadores ni componentes no redistribuibles. Cada ajuste explica su efecto, riesgo y reversión. Si algo no es viable o requiere licencia, se dice claramente.
