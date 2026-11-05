# Rendimiento en AUREVIX OS

## Principios

- **Sin promesas de FPS.** Los ajustes de Windows suelen aportar entre un 0 y un 10 % de FPS medios y, sobre todo, **mejores 1 % lows y menos latencia**. Las grandes subidas vienen de funciones del driver (AFMF, DLSS/FSR/XeSS, generación de fotogramas).
- **Todo se mide** con AUREVIX Benchmark (PresentMon de Intel) antes y después.
- **Todo se deshace**: cada cambio guarda los valores previos (usuario en `%LOCALAPPDATA%\Aurevix\backups`, sistema en `%ProgramData%\Aurevix\service\backups`, solo accesible por SYSTEM/Administradores).
- **Nunca** se desactivan Defender, Firewall, Windows Update, SmartScreen, UAC ni las mitigaciones de Spectre/Meltdown, ni se arrancan componentes de la imagen de Windows (rompe las actualizaciones y no da FPS medibles). Las pruebas automáticas lo verifican.

## Niveles

| Nivel | Para quién |
|---|---|
| **R** — Recomendado | Cualquier equipo. Riesgo bajo. |
| **P** — Rendimiento | Jugar. Incluye Recomendado. |
| **M** — Máximo | Competitivo / entusiastas. Más consumo. Incluye Rendimiento. |
| **Personalizado** | Cada ajuste por separado. Los *opt-in* (reducen seguridad) solo aquí, con aviso. |

Los ajustes con requisitos (sobremesa, SSD, RAM) se omiten solos si no encajan con el equipo.

## Catálogo (45 ajustes)

| Id | Ajuste | Niveles | Riesgo | Reinicio |
|---|---|---|---|---|
| `gaming.game-mode.on` | Modo de juego de Windows | R P M | bajo |  |
| `gaming.background-recording.off` | Sin grabación en segundo plano (Game DVR) | P M | bajo |  |
| `gaming.mouse-acceleration.off` | Sin aceleración del ratón | P M | bajo |  |
| `gaming.mmcss.games` | Prioridad alta para juegos (MMCSS) | P M | bajo | sí |
| `gaming.system-responsiveness` | Menos CPU reservada para tareas en segundo plano | P M | bajo | sí |
| `gaming.network-throttling.off` | Sin limitación de red multimedia | P M | bajo | sí |
| `gaming.foreground-boost` | Más prioridad a la aplicación en primer plano | P M | bajo | sí |
| `gaming.timer-resolution.global` | Resolución de temporizador global | M | medio | sí |
| `gpu.hags.on` | Programación de GPU acelerada por hardware | R P M | bajo | sí |
| `gpu.windowed-optimizations.on` | Optimizaciones para juegos en ventana | R P M | bajo |  |
| `power.plan.high-performance` | Plan de energía Alto rendimiento | P | bajo |  |
| `power.plan.ultimate` | Plan de energía Máximo rendimiento | M | medio |  |
| `power.core-parking.off` | Sin aparcamiento de núcleos (con corriente) | P M | bajo |  |
| `power.cpu-min-state.max` | CPU siempre al 100 % de estado mínimo (con corriente) | M | medio |  |
| `power.usb-selective-suspend.off` | Sin suspensión selectiva de USB (con corriente) | P M | bajo |  |
| `power.pcie-aspm.off` | Sin ahorro de energía en PCI Express (con corriente) | P M | bajo |  |
| `power.throttling.off` | Sin limitación de energía de procesos | M | bajo | sí |
| `power.hibernation.off` | Desactivar hibernación | P M | bajo | sí |
| `power.fast-startup.off` | Desactivar inicio rápido | P M | bajo | sí |
| `system.menu-delay` | Menús más rápidos | R P M | bajo |  |
| `system.startup-delay.off` | Sin espera para las aplicaciones de inicio | R P M | bajo |  |
| `system.background-apps.off` | Sin aplicaciones de la Store en segundo plano | P M | bajo |  |
| `system.sysmain.off` | Desactivar SysMain (Superfetch) | M | medio | sí |
| `storage.last-access.off` | No actualizar la fecha de último acceso (NTFS) | P M | bajo | sí |
| `storage.storage-sense.on` | Sensor de almacenamiento | R P M | bajo |  |
| `explorer.folder-discovery.off` | Explorador más rápido en carpetas grandes | R P M | bajo |  |
| `interface.file-extensions.show` | Mostrar las extensiones de archivo | R P M | bajo |  |
| `interface.start-suggestions.off` | Sin sugerencias ni promociones | R P M | bajo |  |
| `interface.widgets.off` | Desactivar widgets | R P M | bajo |  |
| `interface.transparency.off` | Sin efectos de transparencia | M | bajo |  |
| `interface.transparency.on` | Efectos de transparencia | personalizado | bajo |  |
| `interface.minimize-animation.off` | Sin animación al minimizar | M | bajo |  |
| `privacy.diagtrack.off` | Telemetría opcional desactivada | R P M | bajo | sí |
| `privacy.dmwappush.off` | Servicio de enrutamiento de mensajes WAP desactivado | P M | bajo | sí |
| `privacy.telemetry-tasks.off` | Tareas programadas de telemetría desactivadas | R P M | bajo |  |
| `privacy.copilot.off` | Desactivar Copilot de Windows | R P M | bajo |  |
| `privacy.recall.off` | Desactivar Recall (instantáneas de IA) | R P M | bajo | sí |
| `privacy.web-search.off` | Búsqueda de Inicio solo en tu PC | R P M | bajo |  |
| `privacy.advertising-id.off` | Sin identificador de publicidad | R P M | bajo |  |
| `privacy.tailored-experiences.off` | Sin experiencias personalizadas con diagnósticos | R P M | bajo |  |
| `maintenance.delivery-optimization.p2p-off` | Actualizaciones sin compartir con otros PCs | R P M | bajo | sí |
| `network.nagle.off` | Sin retraso de Nagle en TCP (todas las tarjetas de red) | P M | bajo | sí |
| `network.update-bandwidth.limit` | Limitar las descargas de Windows Update en segundo plano | P M | bajo | sí |
| `network.qos-reserve.off` | Quitar el límite de ancho de banda reservable (QoS) | personalizado | bajo |  |
| `security.memory-integrity.off` | Desactivar Integridad de memoria (HVCI) | opt-in | alto | sí |

## Más allá del catálogo

- **Game Boost**: al detectar un juego de la lista, prioridad alta al juego, prioridad mínima y RAM liberada en apps de fondo, plan de energía de máximo rendimiento; todo se restaura al cerrar el juego.
- **Red**: test real de DNS (UDP) y cambio a Cloudflare/Google/Quad9/OpenDNS con restauración del original; ping, jitter y pérdida; QoS DSCP 46 por juego (efectivo solo si el router respeta QoS); restablecer TCP y reparar red.
- **Limpieza**: apps preinstaladas (reinstalables desde la Store), programas de inicio (mecanismo `StartupApproved` del Administrador de tareas) y temporales.
- **Mantenimiento**: limpieza de componentes (DISM), TRIM del SSD, caché DNS.

## Lo que se ha descartado y por qué

| Idea | Motivo |
|---|---|
| «Duplicar FPS» con ajustes de Windows | No existe evidencia; las comparativas de Atlas/Revi muestran ganancias pequeñas en FPS medios. |
| Quitar Defender / Windows Update (Atlas, KernelOS…) | Seguridad. Principio obligatorio del proyecto. |
| Mitigaciones Spectre/Meltdown desactivadas | Seguridad. |
| «Liberar el 20 % de ancho de banda reservado» | Mito: la propia Microsoft lo desmiente. Se incluye como ajuste personalizado explicado, sin prometer nada. |
| Modificar la imagen de Windows (tiny11 y similares) | Rompe actualizaciones y licencias; no da FPS medibles. |
| Copiar ajustes opacos de otras distribuciones | Solo se incluyen ajustes documentados y reversibles que AUREVIX puede auditar. |

## Fuentes consultadas

- Atlas OS — ajustes de rendimiento: https://www.mintlify.com/Atlas-OS/Atlas/reference/tweaks/performance
- ReviOS — funciones: https://revi.cc/docs/faq/before/features
- Comparativa AtlasOS vs ReviOS: https://www.profesionalreview.com/2025/12/25/atlasos-vs-revios/
- Blur Busters — impacto real en juegos: https://forums.blurbusters.com/viewtopic.php?p=95005
- Microsoft — Integridad de memoria y VMP en juegos (cobertura): https://www.techspot.com/news/96246-microsoft-releases-guide-optimize-windows-11-gaming-performance.html
- Mito del 20 % QoS (KB de Microsoft reproducido): https://tweaks.com/windows/37125/qos-myth/
- QoS DSCP en Xbox: https://learn.microsoft.com/en-us/gaming/gdk/_content/gc/networking/overviews/qos-packet-tagging
- Nagle/TcpNoDelay: https://allthings.how/reduce-windows-11-network-latency-with-the-tcpnodelay-registry-tweak/
- PresentMon: https://github.com/GameTechDev/PresentMon
