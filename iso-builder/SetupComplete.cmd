@echo off
rem AUREVIX OS — se ejecuta al final de Windows Setup (antes del primer inicio de sesión), como SYSTEM.
rem Instala AUREVIX Control Center. En el primer inicio de sesión se abrirá el asistente AUREVIX Setup.
rem Si algo falla, Windows sigue funcionando con normalidad: este script nunca bloquea la instalación.
set LOG=%SystemDrive%\AUREVIX\setup.log
echo [%date% %time%] SetupComplete iniciado >> "%LOG%"
if exist "%SystemDrive%\AUREVIX\AurevixSetup.exe" (
  "%SystemDrive%\AUREVIX\AurevixSetup.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS=desktopicon /LOG="%SystemDrive%\AUREVIX\installer.log" >> "%LOG%" 2>&1
  echo [%date% %time%] Instalador terminó con codigo %errorlevel% >> "%LOG%"
) else (
  echo [%date% %time%] No se encontro el instalador de AUREVIX >> "%LOG%"
)
exit /b 0
