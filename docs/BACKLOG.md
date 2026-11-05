# Backlog por hitos

Leyenda: ✅ hecho y probado · 🟡 escrito, pendiente de probar en Windows · ⬜ pendiente

## Hito 0 — Diseño
- ✅ Arquitectura, ADR, estructura del repositorio, manifiesto de actualizaciones, backlog

## Hito 1 — Prototipo
- 🟡 Control Center con navegación y 9 secciones
- ✅ Lógica de diagnóstico (analizador) · 🟡 recopilación WMI y escritura de informes
- ✅ Logs con retención

## Hito 2 — Configuración reversible
- ✅ Motor de ajustes: vista previa, copia de seguridad, validación, reversión automática
- ✅ Catálogo inicial (10 ajustes HKCU) y 4 perfiles
- ⬜ Ampliar el catálogo: inicio y segundo plano, red, mantenimiento (cada ajuste con validación en VM)
- ⬜ Ajustes con permisos de administrador (dependen del Hito 4)

## Hito 3 — Apariencia
- ✅ Tema oscuro/claro, modo ligero/elegante, alineación de la barra de tareas, 3 fondos, restaurar original
- ⬜ Evaluar herramientas de terceros para el shell (licencia, reputación, compatibilidad, modo de recuperación). Ver ADR 0005
- ⬜ Iconos, cursores, sonidos y pantalla de bloqueo

## Hito 4 — Servicio
- ⬜ AUREVIX Core Service (cuenta de servicio con privilegios mínimos) + API local autenticada (named pipe con ACL)
- ⬜ Mover a él las operaciones elevadas (punto de restauración, ajustes HKLM, instalación de actualizaciones)

## Hito 5 — Actualizador
- ✅ Manifiesto firmado, anti-downgrade, caducidad, rollout, canales, descarga verificada, historial
- 🟡 Ventana automática de actualización y rollback manual
- ⬜ Instalación transaccional con rollback automático si la nueva versión no arranca
- ⬜ Autenticación del dispositivo y paquetes `themePack` / `tweakCatalog`
- ⬜ Firma Authenticode del instalador

## Hito 6 — Instalación
- 🟡 Launcher Installer (Inno Setup) y primer inicio
- 🟡 Web de descargas

## Hito 7 — ISO de laboratorio
- 🟡 ISO Builder (autounattend + `$OEM$` + oscdimg)
- ⬜ Probar en VM; hacer el proceso reproducible; verificación de integridad en CI

## Hito 8 — Candidata para el GMKtec K12
- ⬜ Validar en hardware real: drivers AMD, gráficos, Ethernet/Wi-Fi, USB4, vídeo, suspensión
- ⬜ Temperaturas: integrar una fuente fiable y probada para este hardware (hoy se muestra "No disponible")
- ⬜ Uso de GPU

## Transversal
- ⬜ Pruebas de UI automatizadas en Windows
- ⬜ Telemetría opcional anónima (con pantalla de "qué se envía" antes de activarla)
- ⬜ Localización (hoy solo español)
