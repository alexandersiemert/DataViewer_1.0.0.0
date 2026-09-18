# SIEMERT DataViewer

Windows-Anwendung zum Auslesen und Auswerten von Aufzeichnungen der SIEMERT-Datenlogger
(Baureihe SI-TL1): Luftdruck, Temperatur und Beschleunigung in drei Achsen, abgetastet mit 4 Hz.

> **Zweckbestimmung.** Das Programm dient der nachträglichen Auswertung für Ausbildung,
> Debriefing und Materialbeobachtung. Es ist kein Höhenmesser, kein Sicherungsgerät und nicht für
> Entscheidungen während des Sprungs bestimmt. Die Grenzen der Messung stehen in
> [`Dokumentation/Messgenauigkeit.txt`](Dokumentation/Messgenauigkeit.txt) und sind vor der
> Verwendung zu lesen.

---

## Aufbau

| Projekt | Zweck |
|---|---|
| `src/DataViewer.Core` | Fachkern: Geräteprotokoll, Höhenrechnung, Signalverarbeitung, Sprungerkennung, Dateiformat, serielle Anbindung. Ohne Oberflächenbezug und vollständig testbar. |
| `src/DataViewer.App` | WPF-Oberfläche (.NET 10), Diagramm über ScottPlot 5. |
| `tests/DataViewer.Core.Tests` | 36 Tests gegen aufgezeichnete Gerätedaten, dazu 7 Tests gegen echte Hardware. |
| `installer/` | Inno-Setup-Skript und Erzeugungsskript. |
| `Dokumentation/` | Bedienungsanleitung, Messgenauigkeit, Lizenzbedingungen, Fremdlizenzen. |
| `legacy/v1/` | Quellstand der Fassung 1, nicht mehr gepflegt. Siehe dortige README. |

Der Fachkern kennt die Oberfläche nicht. Dadurch lässt sich jede Rechnung — Höhenbezug,
Ableitung der Sinkrate, Sprungerkennung, Dekodierung — gegen bekannte Sollwerte prüfen, statt
sich auf Sichtprüfung im laufenden Programm zu verlassen.

## Voraussetzungen

**Zum Bauen:** .NET-SDK 10 und Visual Studio 2022+ oder das SDK allein. Für das Setup zusätzlich
[Inno Setup 6](https://jrsoftware.org/isinfo.php).

**Beim Kunden:** nichts. Die Anwendung wird eigenständig ausgeliefert und bringt die Laufzeit mit.
Die Installation kommt ohne Administratorrechte aus.

## Bauen

```powershell
dotnet build SiemertDataViewer.sln
dotnet test  SiemertDataViewer.sln --filter "Category!=Hardware"
```

Auslieferbares Setup einschließlich Tests, Veröffentlichung und Selbsttest:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

Das Skript bricht ab, wenn ein Test fehlschlägt. Ergebnis liegt in `installer_out/`.

## Tests gegen echte Hardware

Setzen einen angeschlossenen Logger voraus:

```powershell
dotnet test SiemertDataViewer.sln --filter "Category=Hardware"
```

Geprüft werden unter anderem: Erkennung über die USB-Kennung, Unversehrtheit einer laufenden
Übertragung während einer Gerätesuche, Abbrechbarkeit, und dass das Gerät mehr Daten sendet als
es ankündigt.

## Selbsttest

Wertet eine Rohdatei ohne Oberfläche aus — für den Kundendienst und als Bauprüfung:

```powershell
SiemertDataViewer.exe --selftest <Rohdatei> [Ziel.png]
```

Rückgabewert 0 bei Erfolg. Geprüft werden Dekodierung, Höhenrechnung, Sprungerkennung,
Zeichenpfad und der Dateipfad einschließlich Prüfsumme.

## Ablageorte beim Anwender

Alles im Benutzerprofil, nichts davon braucht erhöhte Rechte:

```
%LOCALAPPDATA%\SIEMERT\DataViewer\Rohdaten\      jeder Auslesevorgang, unverändert
%LOCALAPPDATA%\SIEMERT\DataViewer\Protokoll\     Tagesprotokolle, Aufbewahrung 60 Tage
%LOCALAPPDATA%\SIEMERT\DataViewer\einstellungen.json
```

Das Programm arbeitet vollständig örtlich und übermittelt keine Daten.

## Vor der Auslieferung zu erledigen

- [ ] **Code-Signing-Zertifikat beschaffen und einbinden.** Ohne Signatur zeigt Windows beim
      Setup „Unbekannter Herausgeber". In verwalteten Umgebungen wird die Installation dadurch
      unter Umständen ganz blockiert. In `installer/SiemertDataViewer.iss` ist die Zeile
      `SignTool=siemert` dafür vorbereitet.
- [ ] **Lizenzbedingungen anwaltlich prüfen lassen.**
      `Dokumentation/Lizenzbedingungen.txt` ist ein fachlich vorbereiteter Entwurf mit einem
      deutlich gekennzeichneten Hinweisblock, der vor der Auslieferung zu entfernen ist.
- [ ] **Gerätekennwerte ergänzen.** Genauigkeit, Drift und Temperaturgang des Drucksensors sowie
      die Ganggenauigkeit der Zeitbasis fehlen in `Dokumentation/Messgenauigkeit.txt` noch. Ohne
      diese Zahlen sind die absoluten Fehlergrenzen des Geräts nicht beziffert.
- [ ] **Signaturschlüssel aus dem Arbeitsverzeichnis entfernen.** Im Wurzelverzeichnis liegen
      mehrere `.pfx`-Dateien. Sie sind nicht in der Versionsverwaltung, gehören aber auf ein
      Token oder in einen geschützten Speicher, nicht neben den Quelltext.
- [ ] **Befehl zum Stellen der Loggeruhr nachreichen.** Die Funktion ist in der Oberfläche
      angelegt und meldet bis dahin ehrlich, dass sie noch nicht freigeschaltet ist.

## Fremdkomponenten

ScottPlot 5 und ScottPlot.WPF (MIT), .NET 10 (MIT). Vollständige Lizenztexte in
`Dokumentation/Fremdkomponenten_Lizenztexte.txt`.
