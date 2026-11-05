# ADR 0007 — AUREVIX Installer, USB, reinstalación limpia y Estilo AUREVIX

**Estado:** aceptada

## Contexto
Se pide: ISO de AUREVIX descargable directamente, un launcher con dos modos (añadir a Windows / reinstalar todo solo), USB detectado automáticamente, instalación en español más automática y un Windows visualmente minimalista.

## Decisiones
1. **No se publica una ISO de Windows** (licencia de Microsoft; además 7 GB > límite de 2 GB de GitHub Releases). En su lugar, **AUREVIX Installer** crea la ISO o el USB en el PC del usuario a partir de la ISO oficial, descargada de Microsoft con **Fido** (GPL-3.0, sin modificar, hash fijado).
2. **Reinstalación limpia** solo con el Windows Setup oficial (`/auto clean /unattend /postoobe`), con confirmación explícita (`BORRAR`) y comprobaciones previas.
3. **USB**: solo discos `BusType = USB` que no sean de arranque ni sistema, revalidados justo antes de borrar; FAT32 (≤31 GB) + `install.swm` con DISM, sin modificar la imagen.
4. **Instalación automática**: idioma, región, teclado, zona horaria y privacidad (opciones oficiales). Siguen siendo manuales la clave, la **licencia**, el **disco** y la **cuenta**.
5. **Estilo AUREVIX**: solo mecanismos oficiales (tema `.theme`, valores de usuario documentados, API `LockScreen`, `WM_SETTINGCHANGE`), por grupos independientes con copia de seguridad y restauración. **No** se reemplaza ni parchea el shell, el Explorador ni Configuración: Windows no ofrece una vía soportada para rediseñarlos ni añadirles animaciones nuevas; se activan las animaciones y transparencias propias de Windows.
6. **TranslucentTB** (GPL-3.0, Microsoft Store) como extra **opcional** de terceros, instalado con winget tras confirmación.
