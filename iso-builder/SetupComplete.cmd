@echo off
rem AUREVIX OS — se ejecuta al final de Windows Setup (antes del primer inicio de sesión), como SYSTEM.
rem Busca el instalador de AUREVIX en C:\AUREVIX (USB/ISO) o junto a este script (reinstalación limpia /PostOOBE).
rem Si algo falla, Windows sigue funcionando con normalidad: este script nunca bloquea la instalación.
if not exist "%SystemDrive%\AUREVIX" mkdir "%SystemDrive%\AUREVIX"
set LOG=%SystemDrive%\AUREVIX\setup.log
echo [%date% %time%] SetupComplete iniciado >> "%LOG%"
rem Sistema optimizado desde el primer arranque: quita apps promocionales preinstaladas para los usuarios nuevos.
rem Solo apps que se pueden reinstalar desde Microsoft Store. No toca la Store, Defender, Fotos, Calculadora ni Terminal.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ids='Microsoft.BingNews','Microsoft.BingSearch','Microsoft.GetHelp','Microsoft.Getstarted','Microsoft.MicrosoftSolitaireCollection','Microsoft.MicrosoftOfficeHub','Microsoft.People','Microsoft.PowerAutomateDesktop','Microsoft.WindowsFeedbackHub','Microsoft.WindowsMaps','Clipchamp.Clipchamp','Microsoft.549981C3F5F10','Microsoft.Windows.DevHome','MicrosoftCorporationII.MicrosoftFamily'; Get-AppxProvisionedPackage -Online | Where-Object { $ids -contains $_.DisplayName } | ForEach-Object { try { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction Stop | Out-Null; Write-Output ('Quitada: ' + $_.DisplayName) } catch {} }" >> "%LOG%" 2>&1
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
