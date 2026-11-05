# ADR 0006 — Diagnóstico local y sensores

**Estado:** aceptada

## Decisión
- El diagnóstico es **solo lectura** y se procesa **en local**; nada se envía fuera del equipo.
- Fuentes: WMI/CIM (`Win32_*`, `MSFT_PhysicalDisk`) y la API COM `Microsoft.Update.Session` en modo sin conexión (caché local).
- Niveles: *Correcto*, *Revisar*, *Requiere atención*, y **No disponible** cuando no se puede leer algo. Los indicios nunca se presentan como fallos confirmados.
- **Temperaturas y uso de GPU: no se muestran.** No existe una API estándar, fiable y sin privilegios para todos los fabricantes (`MSAcpi_ThermalZoneTemperature` es inconsistente y requiere administrador). Se muestra "No disponible" en lugar de inventar valores.
- El estado SMART se limita a la salud que Windows expone (`HealthStatus`); AUREVIX no diagnostica la salud física del disco por su cuenta.

## Pendiente (Hito 8)
Evaluar una fuente de sensores fiable para el hardware objetivo (GMKtec K12, Ryzen 7 H255 / 8745HS, Radeon 780M), con licencia compatible y probada en el equipo real.
