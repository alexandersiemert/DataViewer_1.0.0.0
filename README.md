# SIEMERT DataViewer

Windows-Desktop-Anwendung (WPF, .NET Framework 4.8) zum Auslesen und Auswerten von
SIEMERT-Datenlogger-Aufnahmen (Höhe, Temperatur, 3-Achsen-Beschleunigung).

## Funktionen

- **Geräteerkennung**: automatische Suche kompatibler Logger an den COM-Ports.
- **Auslesen**: gesamten Speicher (*Read all*) oder letzte Aufnahme (*Read last*) über das
  Kontextmenü eines Geräts im Device-Tree.
- **Plot**: interaktive Darstellung via ScottPlot 5 mit getrennten Achsen für Höhe,
  Temperatur und Beschleunigung (Betrag sowie X/Y/Z).
- **Analyse-Werkzeuge**: Measuring Cursor (Min/Max/Delta/Mittelwert), Crosshair,
  Marker, Hover-Tooltip, Legende.
- **Darstellung**: Chart-Stile (Light/Dark/Slate), Serienfarben, Linienbreite/-muster,
  Gitter, Achsenbeschriftungen, zeitbasiertes Smoothing pro Serie.
- **Einheiten**: metrisch (m, °C) oder imperial (ft, °F).
- **Dateien**: Speichern/Laden als `*.sdvlog` (XML), Import von Rohdaten `*.lgd`,
  CSV-Export einzelner Aufnahmen (auch über das Kontextmenü der Aufnahme).

## Build

Voraussetzungen: Visual Studio 2022+ mit .NET-Desktop-Workload (.NET Framework 4.8).

```powershell
# Release-Build
msbuild DataViewer_1.0.0.0.sln /t:Build /p:Configuration=Release
```

Das Ergebnis liegt unter `bin\Release\`.

## Installer

Der Installer wird mit [Inno Setup](https://jrsoftware.org/isinfo.php) aus
`DataViewer_setup.iss` erzeugt und bündelt den Inhalt von `bin\Release\`.

```powershell
# Beispiel (Pfad zu ISCC.exe ggf. anpassen)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" DataViewer_setup.iss
```

Die Ausgabe (`installer_out/`) ist bewusst **nicht** Teil des Repositories –
Setups werden als Release-Artefakte verteilt.

> **Hinweis Roll-out:** Für die Auslieferung an Kunden sollten `.exe` und Installer
> mit einem Code-Signing-Zertifikat signiert werden, um SmartScreen-Warnungen
> ("Unbekannter Herausgeber") zu vermeiden.

## Logs

Zur Fehlersuche schreibt die Anwendung ein Tageslog nach:

```
%LocalAppData%\SIEMERT\DataViewer\logs\dataviewer-yyyyMMdd.log
```

Unbehandelte Fehler werden dort protokolliert, ohne die Anwendung zu beenden.

## Projektstruktur (Kurzüberblick)

| Datei                     | Zweck                                                        |
|---------------------------|-------------------------------------------------------------|
| `MainWindow.xaml(.cs)`    | Hauptfenster, Plot, UI-Logik, Datei-Operationen             |
| `SerialPortManager.cs`    | COM-Port-Kommunikation und Empfangs-/Fortschrittslogik      |
| `ComPortChecker.cs`       | Erkennung kompatibler Geräte an den COM-Ports               |
| `DataLogger(.Manager).cs` | Gerätemodell und Verwaltung je COM-Port                     |
| `TreeViewManager.cs`      | Device-Tree, Kontextmenüs, Serien-Umschaltung               |
| `SiemertDataViewerLog.cs` | DTOs für das `*.sdvlog`-Dateiformat                         |
| `Logger.cs`               | Datei-Logging                                               |
| `Theme/`                  | Zentrale WPF-Styles und Farbpalette                         |

## Dritthersteller-Lizenzen

Siehe `Docs/ThirdParty_Notices.txt` (ScottPlot, ScottPlot.WPF, FontAwesome.WPF).
