# ADR 0004 — ISO, instalación y licencias

**Estado:** aceptada

## Contexto
El objetivo es poder instalar AUREVIX OS desde un USB. La licencia de Windows no permite redistribuir imágenes modificadas.

## Decisión
1. **No se publica ninguna ISO de Windows modificada.**
2. Se ofrecen dos vías legales:
   - **Launcher Installer**: se ejecuta sobre un Windows 11 ya instalado.
   - **ISO Builder**: el usuario aporta la ISO oficial de Microsoft; el script la convierte en local, en su PC. La ISO resultante es para su uso.
3. La modificación se limita a **añadir** archivos: `autounattend.xml` (solo idioma + respaldo de instalación), y `$OEM$` (`SetupComplete.cmd` + instalador). No se toca `install.wim`.
4. Quedan fuera por diseño: claves, activación, salto de requisitos (TPM/Secure Boot), formateo automático del disco, ocultar la licencia.

## Mecanismos de primer inicio
`SetupComplete.cmd` (SYSTEM, al final de Setup) instala la app; `FirstLogonCommands` del `autounattend.xml` es el respaldo y abre el asistente. En ambos casos el usuario puede omitir la personalización.

## Consecuencias
- La construcción de la ISO ocurre en el PC del usuario y requiere el Windows ADK (`oscdimg`).
- No podemos ofrecer "descarga directa de la ISO de AUREVIX".
