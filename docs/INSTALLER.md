# AUREVIX Installer

Un único `AurevixInstaller-x.y.z.exe` (pide permisos de administrador) con tres opciones:

| Opción | Qué hace | ¿Borra algo? |
|---|---|---|
| **1. Instalar en este Windows** | Descarga la última versión verificada de AUREVIX (manifiesto firmado + SHA-256) y abre su instalador. | No |
| **2. Reinstalar este PC con AUREVIX OS** | Descarga la ISO **oficial** de Windows 11 en español de Microsoft, la monta y lanza el **Windows Setup oficial** con `setup.exe /auto clean /unattend /postoobe`. AUREVIX se instala al final. Sin USB. | **Sí**: programas, ajustes y archivos de la unidad del sistema. Exige escribir `BORRAR`, cargador conectado y 25 GB libres; avisa si hay BitLocker. |
| **3. Crear USB o ISO** | Detecta los pendrives al enchufarlos (cada 2 s, solo discos USB que no sean de sistema). Formatea en FAT32, copia Windows, divide `install.wim` con DISM (FAT32 no admite >4 GB) y añade AUREVIX. O bien genera una ISO con `oscdimg` (instala Windows ADK con winget si falta). | El pendrive elegido, tras confirmación |

La ISO se obtiene con **Fido** (GPL-3.0, autor de Rufus; incluido sin modificar en `third_party/fido`, verificado por SHA-256), que solo pide a Microsoft su enlace oficial. Si Microsoft limita la descarga automática, el Installer lo explica y permite usar una ISO descargada a mano.

## Qué pasa al instalar desde el USB o la ISO

1. Arranque desde el USB → Windows Setup **ya en español**.
2. **Clave de producto** (o *No tengo clave*) → **edición** → **licencia de Microsoft** (lo eliges tú).
3. **Disco**: lo eliges tú. AUREVIX nunca formatea un disco por su cuenta.
4. Windows se copia y reinicia solo.
5. Al final de Setup, `SetupComplete.cmd` instala AUREVIX en silencio (`C:\AUREVIX\setup.log`).
6. Pantallas de bienvenida de Windows: **región, teclado y privacidad ya configurados** (es-ES, zona *Romance Standard Time*, diagnósticos opcionales desactivados). Solo verás **red** y **cuenta**.
7. Escritorio → asistente AUREVIX (7 pasos): bienvenida, diagnóstico, informe, punto de restauración, perfil, **estilo AUREVIX**, resumen.

## Limitaciones conocidas (pendiente de probar en Windows real)

- `setup.exe /auto clean` debe validarse en VM en las versiones actuales de Windows 11; si falla, el Installer lo indica y propone el USB.
- El arranque BIOS antiguo usa `bootsect`; los equipos UEFI arrancan desde `\efi\boot`. Secure Boot no se toca.
- El Installer no está firmado con Authenticode: SmartScreen mostrará un aviso.
