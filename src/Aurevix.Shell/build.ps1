# Compila AurevixShell.dll (x64, runtime estático) con el compilador de Visual Studio del equipo.
param([string]$Out = "out\shell")
$ErrorActionPreference = 'Stop'
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
Import-Module (Join-Path $vs 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
New-Item -ItemType Directory -Force $Out | Out-Null
$src = Join-Path $PSScriptRoot 'AurevixShell.cpp'
$def = Join-Path $PSScriptRoot 'AurevixShell.def'
$obj = Join-Path $env:TEMP 'aurevixshell-obj'
New-Item -ItemType Directory -Force $obj | Out-Null
cl /nologo /LD /MT /O2 /EHsc /std:c++20 /permissive- /bigobj /utf-8 /W3 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX `
   "/Fo$obj\" $src /link /DEF:$def windowsapp.lib ole32.lib advapi32.lib user32.lib shlwapi.lib shcore.lib "/OUT:$(Join-Path $Out 'AurevixShell.dll')" "/IMPLIB:$obj\AurevixShell.lib"
if ($LASTEXITCODE -ne 0) { throw "AurevixShell.dll no compiló ($LASTEXITCODE)" }
Copy-Item (Join-Path $PSScriptRoot 'start-logo.png') $Out -Force
Get-Item (Join-Path $Out 'AurevixShell.dll') | Format-Table Name, Length
