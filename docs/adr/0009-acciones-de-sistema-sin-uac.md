# ADR 0009 — Acciones de sistema sin avisos de UAC

**Estado:** aceptada

## Contexto
AUREVIX se presenta como parte del sistema. Pedir UAC para crear un punto de restauración, bloquear un programa
en el Firewall, medir FPS o instalar una actualización rompía esa experiencia.

## Decisión
El Control Center sigue sin privilegios. AUREVIX Core Service (LocalSystem, named pipe solo local e interactivo)
hace estas acciones con entradas muy acotadas y validadas en `CoreRequestHandler`:

| Operación | Entrada del cliente | Validación |
|---|---|---|
| `restore-point` | descripción | solo letras, números, espacio, `-._`; 60 caracteres |
| `firewall-block` | ruta de un .exe | absoluta, `.exe`, sin comillas ni `..`, ni UNC; fuera de Windows, Defender y AUREVIX |
| `firewall-unblock-all` / `firewall-list` | — | solo reglas del grupo `AUREVIX` |
| `benchmark` | nombre de .exe y segundos | `IsValidExeName`, 10-300 s; PresentMon desde la carpeta del programa |
| `update-install` | canal e id de instalación | canal de lista cerrada; el servicio descarga y verifica el **manifiesto firmado** y el SHA-256 él mismo |

Tras una actualización, el instalador reinicia el servicio y el servicio nuevo abre el Control Center en la sesión
del usuario (`WTSQueryUserToken` + `CreateProcessAsUser`).

## Lo que NO se hace sin UAC
Las **exclusiones de Defender** reducen la seguridad: si el servicio las aceptara, cualquier programa del usuario
podría pedirle que Defender dejara de vigilar su carpeta. Siguen pidiendo confirmación a Windows.
