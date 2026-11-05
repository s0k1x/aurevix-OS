# Construir la ISO de AUREVIX OS

## Requisitos

- Windows 10/11 con permisos de administrador y 16 GB libres.
- ISO **oficial** de Windows 11 x64 (de [microsoft.com/software-download/windows11](https://www.microsoft.com/software-download/windows11)).
- **Windows ADK → Deployment Tools** (aporta `oscdimg.exe`): `winget install Microsoft.WindowsADK` y marca solo *Deployment Tools*.
- El paquete `AurevixIsoBuilder-x.y.z.zip` (de la web o de Releases), que ya incluye el instalador de AUREVIX.

## Uso

1. Descomprime el zip.
2. Doble clic en `Build-AurevixIso.cmd`, arrastra tu ISO de Windows y pulsa Intro. O, en PowerShell:
   ```powershell
   .\Build-AurevixIso.ps1 -SourceIso C:\ISOs\Win11_Spanish_x64.iso
   ```
3. Se genera `AUREVIX_OS_AAAAMMDD.iso` y su `.sha256` junto a la ISO original. El log queda en `build-iso.log`.
4. **Pruébala primero en una VM** ([VM_TESTING.md](VM_TESTING.md), prueba B). Para grabar un USB usa [Rufus](https://rufus.ie) u otra herramienta de confianza.

## Qué modifica (y qué no)

Añade a la ISO:

| Ruta dentro de la ISO | Para qué |
|---|---|
| `autounattend.xml` | Solo idioma + un respaldo que instala AUREVIX en el primer inicio. Comentado línea a línea |
| `sources\$OEM$\$$\Setup\Scripts\SetupComplete.cmd` | Windows lo ejecuta al terminar Setup: instala AUREVIX en silencio |
| `sources\$OEM$\$1\AUREVIX\AurevixSetup.exe` (+ `.sha256`) | Instalador copiado a `C:\AUREVIX` |

**No** toca `install.wim` / `install.esd`, no sustituye ni quita componentes protegidos de Windows, no incluye claves ni activadores, no salta TPM / Secure Boot / requisitos, no oculta la licencia de Microsoft y no formatea discos por su cuenta (Windows Setup te pregunta el disco).

## Qué ocurre al instalar

1. Arrancas desde la ISO/USB. Windows Setup funciona como siempre: eliges disco, aceptas la licencia.
2. Al final de Setup, `SetupComplete.cmd` instala AUREVIX (log en `C:\AUREVIX\setup.log`). Si por cualquier motivo no se ejecuta, `autounattend.xml` lo instala en el primer inicio de sesión.
3. Al llegar al escritorio se abre **AUREVIX Setup** (con la opción *Omitir y entrar en Windows*).

## Licencias

La ISO resultante es **para tu uso** en equipos tuyos. Microsoft no permite redistribuir Windows modificado, por eso AUREVIX publica el *builder* y no la ISO. Windows se activa con tu propia licencia. Ver [ADR 0004](adr/0004-iso-y-licencias.md).

## Limitaciones conocidas

- No probado todavía en un Windows real (ver [BACKLOG.md](BACKLOG.md), Hito 7).
- `SetupComplete.cmd` no se ejecuta en modo auditoría ni en actualizaciones in-place; el respaldo de `FirstLogonCommands` cubre el caso normal.
- La ISO no se firma ni se garantiza reproducible bit a bit (el SHA-256 sirve para verificar tu copia). La reproducibilidad es un objetivo del Hito 7.
