# ADR 0002 — Actualizaciones con manifiesto firmado

**Estado:** aceptada

## Contexto
El cliente debe poder actualizarse desde un servidor propio sin que ese servidor (o un atacante que lo suplante) pueda ejecutar código arbitrario en el PC.

## Decisión
- Un `manifest.json` por canal, firmado con **ECDSA P-256 / SHA-256** sobre sus bytes exactos.
- Claves públicas de confianza **incrustadas en el cliente**; la privada solo existe como secreto de CI.
- Protecciones: caducidad (`expiresAt`), secuencia monotónica (anti-downgrade/replay), hosts de descarga permitidos, HTTPS obligatorio, SHA-256 y tamaño por paquete, despliegue gradual determinista.
- El cliente solo hace peticiones **salientes** y lanza el instalador con **argumentos fijos**.

## Alternativas descartadas
- Confiar solo en HTTPS: no protege frente a un servidor/almacenamiento comprometido.
- Authenticode como única verificación: necesario a futuro (ver backlog), pero no cubre el manifiesto ni el anti-downgrade.
- Comandos remotos: descartado por principio.

## Consecuencias
- Perder la clave privada obliga a una rotación de claves (documentada).
- Sin firma Authenticode, SmartScreen puede avisar al ejecutar el instalador.
