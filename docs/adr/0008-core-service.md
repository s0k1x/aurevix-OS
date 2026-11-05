# ADR 0008 — AUREVIX Core Service

**Estado:** aceptada

## Contexto
Los ajustes profundos (HKLM, servicios, tareas programadas, energía, DNS, QoS) requieren privilegios. La interfaz debe seguir sin ejecutarse como administrador.

## Decisión
- Servicio de Windows `AurevixCoreService` (LocalSystem, inicio automático, reinicio ante fallos), instalado con el Control Center.
- Comunicación por **named pipe local** `AurevixCoreService`: ACL que deniega la red y permite solo usuarios con sesión interactiva; un mensaje JSON por conexión, ≤ 64 KB, 10 s de tiempo máximo.
- **El servicio no acepta datos que se ejecuten o escriban tal cual.** Solo:
  - identificadores de ajustes de **su propio catálogo incrustado** (los valores salen del catálogo);
  - identificadores de copias de seguridad **creadas por el mismo usuario** (SID obtenido del token de la conexión);
  - nombres de una **lista cerrada** de tareas de mantenimiento y de proveedores DNS;
  - nombres de `.exe` validados (sin ruta) para QoS.
- Las copias de seguridad viven en `%ProgramData%\Aurevix\service` con ACL **solo SYSTEM y Administradores**, para que un usuario no pueda colocar una copia falsa.
- Directivas de usuario (`HKCU\Software\Policies`) se escriben en `HKEY_USERS\<SID del cliente>`.
- La parte de usuario (HKCU) la aplica el Control Center; si la parte de sistema falla, se deshace la de usuario.

## Consecuencias
- Un programa malicioso del usuario solo podría activar o desactivar ajustes del catálogo (todos reversibles y documentados), nunca escribir valores arbitrarios ni ejecutar comandos.
- Pruebas automáticas cubren: ids desconocidos, operaciones no permitidas, mantenimiento fuera de lista, DNS fuera de lista, nombres de `.exe` maliciosos, recorrido de rutas y propiedad de las copias.
