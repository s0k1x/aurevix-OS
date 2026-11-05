@echo off
rem Doble clic: pide la ISO oficial de Windows 11 y construye la ISO de AUREVIX OS.
rem Necesita ejecutarse como administrador (montar la ISO y generar la nueva).
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Pidiendo permisos de administrador...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
set /p SRC=Arrastra aqui tu ISO oficial de Windows 11 y pulsa Intro:
set SRC=%SRC:"=%
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-AurevixIso.ps1" -SourceIso "%SRC%"
echo.
pause
