; ============================================================================
;  SIEMERT DataViewer: Installationsskript (Inno Setup 6)
;
;  Erzeugen:  ISCC.exe installer\SiemertDataViewer.iss
;  Erwartet den Inhalt von  publish\  (siehe installer\build.ps1).
;
;  Unterschiede zur Fassung 1:
;   - Der Dateiname der Anwendung enthaelt keine Versionsnummer mehr. Vorher blieben
;     bei einem Update zwei Programmdateien nebeneinander liegen und Verknuepfungen
;     zeigten auf die alte; der Kunde meldete dann "das Update hat nichts geaendert".
;   - Installiert wird der Inhalt von publish\, nicht der von bin\Release\. Dort sammelten
;     sich Reste frueherer Baustaende an, darunter Bibliotheken fuer Linux und macOS.
;   - Die Lizenzbedingungen werden angezeigt und die Zustimmung eingeholt.
;   - Keine Administratorrechte noetig. Die Installation erfolgt benutzerbezogen; wer
;     fuer alle Benutzer installieren will, kann das im Setup waehlen.
;   - Vor dem Kopieren wird das Zielverzeichnis geleert, damit keine Altdateien ueberleben.
; ============================================================================

#define AppName        "SIEMERT DataViewer"
#define AppShortName   "DataViewer"
#define AppPublisher   "SIEMERT"
#define AppExeName     "SiemertDataViewer.exe"
#define AppVersion     "2.0.0"
#define SourceDir      "..\publish"

[Setup]
; Stabil lassen, damit Updates das Vorgaengerprogramm ersetzen statt daneben zu installieren.
AppId={{6F3C9A54-8E21-4B7D-9C10-2A5E7B1D4C88}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppPublisher}\{#AppShortName}
DefaultGroupName={#AppPublisher}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

; Ohne Administratorrechte installierbar. Das ist in verwalteten Umgebungen oft die
; Voraussetzung dafuer, dass die Software ueberhaupt eingesetzt werden darf.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

OutputDir=..\installer_out
OutputBaseFilename={#AppShortName}_Setup_{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Icon.ico

LicenseFile=..\Dokumentation\Lizenzbedingungen.txt
InfoBeforeFile=..\Dokumentation\Hinweis_vor_Installation.txt

CloseApplications=yes
RestartApplications=no

; Fuer die Auslieferung: Zertifikat in Inno Setup unter Werkzeuge, Konfiguration,
; Signaturwerkzeuge hinterlegen und die folgende Zeile aktivieren. Ohne Signatur zeigt
; Windows beim Start des Setups die Warnung "Unbekannter Herausgeber".
; SignTool=siemert

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Verknüpfung auf dem Desktop anlegen"; GroupDescription: "Zusätzliche Aufgaben:"; Flags: unchecked

[InstallDelete]
; Zielverzeichnis vor dem Kopieren leeren. Sonst ueberleben Dateien frueherer Fassungen.
Type: filesandordirs; Name: "{app}"
; Programmdatei der Fassung 1 entfernen, falls an derselben Stelle installiert.
Type: files; Name: "{app}\DataViewer_1.0.0.0.exe"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Dokumentation\*"; DestDir: "{app}\Dokumentation"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Bedienungsanleitung"; Filename: "{app}\Dokumentation\Bedienungsanleitung.txt"
Name: "{group}\Messgenauigkeit und Grenzen"; Filename: "{app}\Dokumentation\Messgenauigkeit.txt"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{#AppName} jetzt starten"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"
