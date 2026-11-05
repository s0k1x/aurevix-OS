# Probar AUREVIX OS en una máquina virtual

**Nunca pruebes una versión experimental en tu único ordenador de uso diario.** Usa una VM con *snapshots*.

## Preparar la VM

1. Instala **Hyper-V** (Windows 11 Pro), **VirtualBox** o **VMware Workstation Player**.
2. Descarga la ISO **oficial** de Windows 11 (x64) de [microsoft.com/software-download/windows11](https://www.microsoft.com/software-download/windows11) y verifica su SHA-256 con el publicado allí.
3. Crea la VM: 4 CPU, 8 GB de RAM, 80 GB de disco, **UEFI + Secure Boot + TPM 2.0** (Hyper-V: *Generación 2* y activar TPM; VirtualBox 7: activar TPM 2.0 y EFI).
4. Instala Windows 11 normalmente. **Haz un snapshot** limpio ("Windows limpio").

## Prueba A — Launcher Installer (la más rápida)

1. En la VM descarga `AurevixSetup-x.y.z.exe` de la web o de GitHub Releases.
2. Ejecútalo (SmartScreen puede avisar: el instalador aún no está firmado con Authenticode → *Más información → Ejecutar de todas formas*).
3. Comprueba:
   - [ ] Se instala en `C:\Program Files\AUREVIX` y crea accesos en Inicio y escritorio.
   - [ ] Se abre el **asistente de primer inicio** (6 pasos).
   - [ ] El diagnóstico detecta CPU, GPU, RAM, discos y red. Las temperaturas aparecen como *No disponible*.
   - [ ] "Crear punto de restauración" pide UAC y funciona (Protección del sistema activada).
   - [ ] Aplicar el perfil *Gaming* y luego *Eficiencia* cambia los valores; *Deshacer perfil* los restaura.
   - [ ] Icono en la bandeja; cerrar la ventana no cierra la app; "Salir" sí.
   - [ ] Tras reiniciar sesión la app arranca en la bandeja.
   - [ ] Desinstalar desde *Configuración → Aplicaciones* quita la app y la clave de inicio.

## Prueba B — ISO Builder + instalación limpia

1. En tu PC con Windows (con el ADK instalado: `winget install Microsoft.WindowsADK`, solo *Deployment Tools*), ejecuta `Build-AurevixIso.cmd` y arrastra tu ISO oficial. Ver [ISO_BUILDER.md](ISO_BUILDER.md).
2. Crea una VM nueva (sin sistema) y arranca con `AUREVIX_OS_*.iso`.
3. Instala Windows: **tú eliges el disco y aceptas la licencia** (el instalador no formatea nada por su cuenta).
4. Comprueba:
   - [ ] En `C:\AUREVIX\setup.log` aparece el resultado de `SetupComplete.cmd`.
   - [ ] Al llegar al escritorio se abre AUREVIX Setup sin intervención.
   - [ ] Si omites el asistente, entras a Windows con normalidad.

## Prueba C — Actualización automática

1. Instala la versión `0.1.0`.
2. Publica `v0.1.1` (ver [RELEASING.md](RELEASING.md)).
3. En la VM, abre *Actualizaciones → Buscar actualizaciones* (o espera a la comprobación automática).
4. Comprueba:
   - [ ] Aparece la **ventana de actualización** con las novedades.
   - [ ] *Más tarde* la cierra; *Omitir esta versión* no vuelve a avisar de esa versión.
   - [ ] *Actualizar ahora* descarga, verifica, pide UAC e instala; la app se reabre en la bandeja con la versión nueva.
   - [ ] Con la red cortada durante la descarga, la versión instalada sigue funcionando.
   - [ ] *Actualizaciones → Reinstalar versión anterior* reinstala la 0.1.0.

## Prueba D — Seguridad del actualizador

- [ ] Un manifiesto con la firma modificada se ignora (aparece "firma no válida" en el log).
- [ ] Un manifiesto con `sequence` menor que el último visto se ignora.
- [ ] Un instalador con el hash alterado se descarta.

## Si algo falla

Abre `%LOCALAPPDATA%\Aurevix\logs\` (también desde *Ajustes → Abrir registros*) y pega el contenido del último log. Para volver atrás: restaura el snapshot de la VM.
