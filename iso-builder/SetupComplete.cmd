@echo off
rem AUREVIX OS — se ejecuta al final de Windows Setup (antes del primer inicio de sesión), como SYSTEM.
rem Busca el instalador de AUREVIX en C:\AUREVIX (USB/ISO) o junto a este script (reinstalación limpia /PostOOBE).
rem Si algo falla, Windows sigue funcionando con normalidad: este script nunca bloquea la instalación.
if not exist "%SystemDrive%\AUREVIX" mkdir "%SystemDrive%\AUREVIX"
set LOG=%SystemDrive%\AUREVIX\setup.log
echo [%date% %time%] SetupComplete iniciado >> "%LOG%"
set INST=
if exist "%SystemDrive%\AUREVIX\AurevixSetup.exe" set INST=%SystemDrive%\AUREVIX\AurevixSetup.exe
if not defined INST if exist "%~dp0AurevixSetup.exe" (
  copy /y "%~dp0AurevixSetup.exe" "%SystemDrive%\AUREVIX\AurevixSetup.exe" >nul
  set INST=%SystemDrive%\AUREVIX\AurevixSetup.exe
)
if defined INST (
  "%INST%" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS=desktopicon /LOG="%SystemDrive%\AUREVIX\installer.log" >> "%LOG%" 2>&1
  echo [%date% %time%] Instalador terminado con codigo %errorlevel% >> "%LOG%"
) else (
  echo [%date% %time%] No se encontro el instalador de AUREVIX >> "%LOG%"
)
exit /b 0
