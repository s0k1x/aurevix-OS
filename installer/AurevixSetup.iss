; AUREVIX OS — Launcher Installer (Inno Setup 6)
; Instala el Control Center sobre un Windows 11 ya instalado. No toca archivos protegidos de Windows.
; Compilar:  iscc /DAppVersion=0.1.0 /DSourceDir=..\out\app /DOutputDir=..\out\installer installer\AurevixSetup.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\app"
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

[Icons]
Name: "{autoprograms}\AUREVIX Control Center"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\AUREVIX Control Center"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Arranque en la bandeja para todos los usuarios. El usuario puede desactivarlo en Ajustes.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AurevixControlCenter"; \
  ValueData: """{app}\{#AppExe}"" --background"; Flags: uninsdeletevalue

[Run]
; Instalación normal: abrir el asistente de primer inicio (o el Control Center si ya se completó).
Filename: "{app}\{#AppExe}"; Description: "Abrir AUREVIX Control Center"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Actualización silenciosa lanzada por AUREVIX Update: volver a dejarlo en la bandeja.
Filename: "{app}\{#AppExe}"; Parameters: "--background"; Flags: nowait runasoriginaluser; Check: IsAurevixUpdate

[Code]
function IsAurevixUpdate: Boolean;
begin
  Result := WizardSilent and (Pos('/AUREVIXUPDATE', UpperCase(GetCmdTail)) > 0);
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
