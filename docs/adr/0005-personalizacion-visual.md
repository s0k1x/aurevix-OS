# ADR 0005 — Personalización visual solo con mecanismos soportados

**Estado:** aceptada

## Decisión
La versión inicial solo usa mecanismos **documentados y soportados**: modo de color de apps y sistema, transparencias, animación de ventanas, alineación de la barra de tareas (Windows 11) y fondo de pantalla (`SystemParametersInfo`). Todo se guarda con copia de seguridad y se puede **restaurar a la apariencia original**.

## Fuera de alcance por ahora
Sustituir o parchear la barra de tareas, el menú Inicio o el Explorador mediante herramientas de terceros (hooks, parcheo de DLL, etc.). Son frágiles ante las actualizaciones de Windows, pueden implicar problemas de licencia y de seguridad, y pueden romper el shell.

## Antes de integrar cualquier herramienta de terceros
Revisar licencia, reputación, compatibilidad con actualizaciones de Windows y riesgos de seguridad; preferir APIs oficiales; asegurar un **modo de recuperación** que restaure el shell estándar si algo falla.

## Consecuencias
La identidad visual de esta versión se apoya en el tema del propio Control Center, los fondos AUREVIX y los ajustes oficiales de Windows, no en un rediseño del shell.
