# AUREVIX OS

Experiencia de **Windows 11** personalizada, modular y actualizable. AUREVIX **no** es un sistema operativo nuevo ni redistribuye Windows: usa la imagen oficial y con licencia de Microsoft y añade, de forma transparente y reversible, un asistente de primer inicio, un Control Center permanente, perfiles de rendimiento y un sistema de actualizaciones firmadas.

> Estado: **versión de laboratorio 0.1.0**. Pruébala siempre primero en una máquina virtual.

## Qué incluye

| Componente | Estado |
|---|---|
| **Control Center** (WPF, .NET 8) con 9 secciones: Inicio, Diagnóstico, Rendimiento, Perfiles, Optimización, Apariencia, Actualizaciones, Recuperación, Ajustes | Escrito. Pendiente de compilar y probar en Windows |
| **AUREVIX Setup** (asistente de primer inicio): diagnóstico, informe, punto de restauración, perfil, resumen | Escrito. Pendiente de probar |
| **Diagnóstico** de solo lectura (WMI) con niveles Correcto / Revisar / Requiere atención / No disponible. Nunca inventa temperaturas | Lógica probada; recopilación pendiente de probar en Windows |
| Perfiles de uso (Equilibrado, Gaming, Productividad, Eficiencia) | Probado |
| **AUREVIX Update**: manifiesto firmado ECDSA P-256, anti-downgrade, caducidad, despliegue gradual, descarga verificada con SHA-256, canales Stable/Beta/Developer | Probado |
| **Pantalla automática de actualización** al publicar una versión nueva | Escrita. Pendiente de probar |
| **AUREVIX Installer**: instalar en este Windows · reinstalar limpio sin USB · crear USB (detección automática) o ISO. Ver [docs/INSTALLER.md](docs/INSTALLER.md) | Escrito. Pendiente de probar en Windows |
| **Instalación en español casi automática** (idioma, región, teclado, zona horaria, privacidad) | Escrito. Pendiente de probar |
| **Estilo AUREVIX**: Windows minimalista en un clic (tema, barra de tareas, Inicio, Explorador, pantalla de bloqueo), reversible | Escrito. Pendiente de probar |
| Instalador del Control Center (Inno Setup) e ISO Builder por script | Publicados en v0.1.0 |
| **Servidor de versiones** (Supabase: tabla `releases` + Storage `releases`) | Desplegado |
| **Web de descargas** (`web/`) | Lista; se publica con GitHub Pages |
| **AUREVIX Core Service**: ajustes de sistema solo del catálogo, API local cerrada. Ver [ADR 0008](docs/adr/0008-core-service.md) | Escrito. Pendiente de probar en Windows |
| **Rendimiento**: 3 niveles + Personalizado (45 ajustes), Benchmark (PresentMon), Game Boost, Red (DNS, ping, QoS), Limpieza. Ver [docs/PERFORMANCE.md](docs/PERFORMANCE.md) | Lógica probada (60 pruebas). Pendiente de probar en Windows |
| Rediseño del shell (sustituir barra de tareas, Inicio, Explorador o Configuración) | **No incluido** a propósito: no hay vía soportada. Ver ADR 0005 y 0007 |
| Temperaturas y uso de GPU | **No incluidos**: no hay una fuente fiable sin privilegios. Ver ADR 0006 |

## Instalar

Descarga **AUREVIX Installer** desde la web (https://s0k1x.github.io/sistema-OS-aurevix-OS/) y elige: instalar en este Windows, reinstalar limpio o crear un USB/ISO. Ver [docs/INSTALLER.md](docs/INSTALLER.md).

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
