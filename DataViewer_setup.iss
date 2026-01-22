#define MyAppName "DataViewer"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SIEMERT"
#define MyAppExeName "DataViewer_1.0.0.0.exe"

[Setup]
; WICHTIG: AppId stabil lassen, damit Updates "drüber installieren" funktionieren
AppId={{8D2A0F7E-2E1B-4E0B-9F6B-2B2E9A9B1C11}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppPublisher}\{#MyAppName}
DefaultGroupName={#MyAppPublisher}\{#MyAppName}

; AdminInstall: für Program Files -> braucht Adminrechte
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; Output
OutputDir=.\installer_out
OutputBaseFilename={#MyAppName}_Setup_{#MyAppVersion}
Compression=lzma2
SolidCompression=yes

; Optik/Infos (optional)
SetupIconFile=.\Icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; Verhindert doppelte Instanzen beim Installieren
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Aufgaben:"; Flags: unchecked

[Files]
; INSTALLIERT ALLES aus bin\Release
Source: ".\bin\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; App nach Installation starten (optional)
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} starten"; Flags: nowait postinstall skipifsilent
