; AUREVIX OS — Launcher Installer (Inno Setup 6)
; Instala el Control Center sobre un Windows 11 ya instalado. No toca archivos protegidos de Windows.
; Compilar:  iscc /DAppVersion=0.1.0 /DSourceDir=..\out\app /DOutputDir=..\out\installer installer\AurevixSetup.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\app"
#endif
#ifndef ServiceDir
  #define ServiceDir "..\out\service"
#endif
#ifndef OutputDir
  #define OutputDir "..\out\installer"
#endif

#define AppName "AUREVIX Control Center"
#define AppExe "AurevixControlCenter.exe"

[Setup]
AppId={{F6F1B20D-0B10-4365-AA08-D851272615C2}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=AUREVIX
DefaultDirName={autopf}\AUREVIX
DefaultGroupName=AUREVIX
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=AurevixSetup-{#AppVersion}
SetupIconFile=..\src\Aurevix.ControlCenter\Assets\aurevix.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
ShowLanguageDialog=no
VersionInfoVersion={#AppVersion}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; AUREVIX Core Service (operaciones de sistema limitadas al catálogo de AUREVIX)
Source: "{#ServiceDir}\*"; DestDir: "{app}\service"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\AUREVIX Control Center"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\AUREVIX Control Center"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Arranque en la bandeja para todos los usuarios. El usuario puede desactivarlo en Ajustes.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AurevixControlCenter"; \
  ValueData: """{app}\{#AppExe}"" --background"; Flags: uninsdeletevalue

[Run]
; Registrar e iniciar AUREVIX Core Service (inicio automático, se reinicia solo si falla).
Filename: "{sys}\sc.exe"; Parameters: "create AurevixCoreService binPath= ""{app}\service\AurevixCoreService.exe"" start= auto DisplayName= ""AUREVIX Core Service"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "config AurevixCoreService binPath= ""{app}\service\AurevixCoreService.exe"" start= auto"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description AurevixCoreService ""Aplica los ajustes de sistema de AUREVIX. Solo acepta ajustes de su catálogo."""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure AurevixCoreService reset= 86400 actions= restart/5000/restart/30000//"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start AurevixCoreService"; Flags: runhidden waituntilterminated
; Instalación normal: abrir el asistente de primer inicio (o el Control Center si ya se completó).
Filename: "{app}\{#AppExe}"; Description: "Abrir AUREVIX Control Center"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Actualización silenciosa lanzada por AUREVIX Update: volver a dejarlo en la bandeja.
Filename: "{app}\{#AppExe}"; Parameters: "--background --after-update"; Flags: nowait runasoriginaluser; Check: IsAurevixUpdate

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop AurevixCoreService"; Flags: runhidden waituntilterminated; RunOnceId: "StopSvc"
Filename: "{sys}\sc.exe"; Parameters: "delete AurevixCoreService"; Flags: runhidden waituntilterminated; RunOnceId: "DelSvc"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code, Waited: Integer;
  TmpFile: String;
  Output: AnsiString;
  Stopped: Boolean;
begin
  // Detener el servicio antes de reemplazar sus archivos (actualizaciones).
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop AurevixCoreService', '', SW_HIDE, ewWaitUntilTerminated, Code);
  // Esperar (máx. 20 s, cada 500 ms) a que el servicio esté detenido o ya no exista.
  TmpFile := ExpandConstant('{tmp}\aurevix-svc.txt');
  Waited := 0;
  Stopped := False;
  while (Waited < 20000) and (not Stopped) do
  begin
    DeleteFile(TmpFile);
    Exec(ExpandConstant('{cmd}'), '/C sc.exe query AurevixCoreService > "' + TmpFile + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, Code);
    if LoadStringFromFile(TmpFile, Output) then
      Stopped := (Pos('STOPPED', Output) > 0) or (Pos('1060', Output) > 0);
    if not Stopped then
    begin
      Sleep(500);
      Waited := Waited + 500;
    end;
  end;
  // Si sigue en marcha, forzar el cierre del ejecutable del servicio.
  if not Stopped then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AurevixCoreService.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(1000);
  end;
  Result := '';
end;

function IsAurevixUpdate: Boolean;
begin
  // Si la actualización la lanza AUREVIX Core Service, es el servicio nuevo quien abre la app en la sesión del usuario.
  Result := WizardSilent and (Pos('/AUREVIXUPDATE', UpperCase(GetCmdTail)) > 0) and (Pos('/AUREVIXSERVICE', UpperCase(GetCmdTail)) = 0);
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsWin64 then
  begin
    MsgBox('AUREVIX OS requiere Windows 11 de 64 bits.', mbError, MB_OK);
    Result := False;
  end;
end;
