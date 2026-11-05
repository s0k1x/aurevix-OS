<#
.SYNOPSIS
  AUREVIX ISO Builder: convierte una ISO OFICIAL de Windows 11 en una ISO de instalación de AUREVIX OS.

.DESCRIPTION
  Qué hace:
    1. Monta tu ISO oficial de Windows 11 (descargada de microsoft.com/software-download/windows11).
    2. Copia su contenido a una carpeta de trabajo.
    3. Añade, SIN modificar install.wim/install.esd ni ningún componente protegido de Windows:
         - autounattend.xml            (solo idioma; el resto de pasos de Windows Setup los haces tú)
         - sources\$OEM$\...           (SetupComplete.cmd + instalador de AUREVIX)
    4. Genera la nueva ISO arrancable (BIOS y UEFI) con oscdimg (Windows ADK) y calcula su SHA-256.

  Qué NO hace: no incluye claves de producto, no activa Windows, no salta requisitos (TPM, Secure Boot),
  no formatea discos por su cuenta, no desactiva funciones de seguridad.

  La ISO resultante es para TU uso. Microsoft no permite redistribuir Windows modificado.

.EXAMPLE
  .\Build-AurevixIso.ps1 -SourceIso C:\ISOs\Win11_25H2_Spanish_x64.iso
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceIso,
    [string]$OutputIso,
    [string]$InstallerPath,
    [string]$WorkDir = (Join-Path $env:TEMP 'aurevix-iso-work'),
    [string]$UiLanguage = 'es-ES',
    [switch]$KeepWorkDir
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$logFile = Join-Path $here 'build-iso.log'
function Log($m) { $l = "$(Get-Date -Format s) $m"; Write-Host $l; Add-Content -Path $logFile -Value $l }

try {
    Log '== AUREVIX ISO Builder =='

    # --- Validaciones ---------------------------------------------------------
    if (-not (Test-Path $SourceIso -PathType Leaf)) { throw "No existe la ISO: $SourceIso" }
    if ([IO.Path]::GetExtension($SourceIso) -ne '.iso') { throw 'El archivo de origen debe ser una .iso' }
    $SourceIso = (Resolve-Path $SourceIso).Path

    if (-not $InstallerPath) {
        $InstallerPath = (Get-ChildItem -Path $here -Filter 'AurevixSetup-*.exe' -File -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1).FullName
    }
    if (-not $InstallerPath -or -not (Test-Path $InstallerPath)) {
        throw 'No se encontró AurevixSetup-*.exe junto al script. Descarga el paquete completo del ISO Builder desde la web de AUREVIX.'
    }
    Log "Instalador de AUREVIX: $InstallerPath"

    if (-not $OutputIso) {
        $OutputIso = Join-Path (Split-Path -Parent $SourceIso) ('AUREVIX_OS_' + (Get-Date -Format 'yyyyMMdd') + '.iso')
    }
    if (Test-Path $OutputIso) { throw "Ya existe $OutputIso. Elige otro nombre con -OutputIso o bórralo tú." }

    # oscdimg (Windows ADK > Deployment Tools)
    $oscdimg = (Get-Command oscdimg.exe -ErrorAction SilentlyContinue).Source
    if (-not $oscdimg) {
        $cand = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\*\Oscdimg\oscdimg.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($cand) { $oscdimg = $cand.FullName }
    }
    if (-not $oscdimg) {
        throw @'
Falta oscdimg.exe (herramienta oficial de Microsoft). Instálala una vez con:
    winget install Microsoft.WindowsADK
(en el instalador marca solo "Deployment Tools") y vuelve a ejecutar este script.
'@
    }
    Log "oscdimg: $oscdimg"

    $free = (Get-PSDrive -Name ([IO.Path]::GetPathRoot($WorkDir).Substring(0, 1))).Free
    if ($free -lt 16GB) { throw 'Se necesitan al menos 16 GB libres en la unidad de trabajo.' }

    Log "SHA-256 de la ISO de origen (compáralo con el publicado por Microsoft):"
    Log ("  " + (Get-FileHash $SourceIso -Algorithm SHA256).Hash)

    # --- Montar y copiar ------------------------------------------------------
    if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
    New-Item -ItemType Directory -Path $WorkDir | Out-Null

    Log 'Montando la ISO de Windows…'
    $img = Mount-DiskImage -ImagePath $SourceIso -PassThru
    try {
        $drive = ($img | Get-Volume).DriveLetter
        if (-not $drive) { throw 'No se pudo obtener la letra de unidad de la ISO montada.' }
        $src = "${drive}:\"
        if (-not (Test-Path (Join-Path $src 'sources\install.wim')) -and -not (Test-Path (Join-Path $src 'sources\install.esd'))) {
            throw 'La ISO no contiene sources\install.wim ni install.esd: no parece una ISO de instalación de Windows.'
        }
        if (-not (Test-Path (Join-Path $src 'bootmgr'))) { throw 'La ISO no es arrancable (falta bootmgr).' }

        Log 'Copiando archivos (puede tardar varios minutos)…'
        & robocopy $src $WorkDir /E /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy falló con el código $LASTEXITCODE" }
    }
    finally {
        Dismount-DiskImage -ImagePath $SourceIso | Out-Null
    }
    # Los archivos copiados de una ISO son de solo lectura.
    & attrib.exe -R "$WorkDir\*" /S /D | Out-Null

    # --- Añadir AUREVIX -------------------------------------------------------
    Log 'Añadiendo AUREVIX a la ISO…'
    $unattend = Get-Content (Join-Path $here 'autounattend.xml') -Raw -Encoding UTF8
    $unattend = $unattend.Replace('{{UI_LANGUAGE}}', $UiLanguage)
    Set-Content -Path (Join-Path $WorkDir 'autounattend.xml') -Value $unattend -Encoding UTF8

    $oemScripts = Join-Path $WorkDir 'sources\$OEM$\$$\Setup\Scripts'
    $oemAurevix = Join-Path $WorkDir 'sources\$OEM$\$1\AUREVIX'
    New-Item -ItemType Directory -Force -Path $oemScripts, $oemAurevix | Out-Null
    Copy-Item (Join-Path $here 'SetupComplete.cmd') (Join-Path $oemScripts 'SetupComplete.cmd')
    Copy-Item $InstallerPath (Join-Path $oemAurevix 'AurevixSetup.exe')
    $instHash = (Get-FileHash $InstallerPath -Algorithm SHA256).Hash
    Set-Content -Path (Join-Path $oemAurevix 'AurevixSetup.exe.sha256') -Value $instHash

    # --- Generar ISO arrancable BIOS + UEFI -----------------------------------
    $etfs = Join-Path $WorkDir 'boot\etfsboot.com'
    $efi = Join-Path $WorkDir 'efi\microsoft\boot\efisys.bin'
    if (-not (Test-Path $etfs) -or -not (Test-Path $efi)) { throw 'Faltan archivos de arranque (etfsboot.com / efisys.bin) en la ISO original.' }

    Log 'Generando la nueva ISO…'
    $label = 'AUREVIX_OS'
    $bootData = '-bootdata:2#p0,e,b"' + $etfs + '"#pEF,e,b"' + $efi + '"'
    $oscArgs = @('-m', '-o', '-u2', '-udfver102', "-l$label", $bootData, $WorkDir, $OutputIso)
    & $oscdimg @oscArgs
    if ($LASTEXITCODE -ne 0) { throw "oscdimg falló con el código $LASTEXITCODE" }

    $hash = (Get-FileHash $OutputIso -Algorithm SHA256).Hash
    Set-Content -Path "$OutputIso.sha256" -Value "$hash  $(Split-Path -Leaf $OutputIso)"
    Log "ISO creada: $OutputIso"
    Log "SHA-256:    $hash"
    Log 'Siguiente paso: pruébala PRIMERO en una máquina virtual. Para grabar un USB usa Rufus (rufus.ie) u otra herramienta de confianza.'
}
catch {
    Log "ERROR: $($_.Exception.Message)"
    exit 1
}
finally {
    if (-not $KeepWorkDir -and (Test-Path $WorkDir)) { Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue }
}
