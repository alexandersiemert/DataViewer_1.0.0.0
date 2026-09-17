# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden hier dokumentiert.
Format angelehnt an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/).

## [Unveröffentlicht]

### Hinzugefügt
- Datei-Logging (`Logger`) nach `%LocalAppData%\SIEMERT\DataViewer\logs\`.
- Globale Fehlerbehandlung: unbehandelte UI- und Hintergrund-Ausnahmen werden
  protokolliert und stürzen die Anwendung nicht mehr kommentarlos ab.
- CSV-Export direkt aus dem Kontextmenü einer Aufnahme im Device-Tree.
- README, CHANGELOG und LICENSE.

### Geändert
- Zentrales WPF-Theme (Farbpalette, Button-/Header-Styles) für ein einheitliches
  Erscheinungsbild.
- Fenstertitel wird konsistent aus der Assembly-Version abgeleitet.
- Assembly-Metadaten (Produkt/Firma/Copyright) gesetzt.

### Behoben
- `File > Close` schließt nun das Fenster (vorher ohne Funktion).
- Nicht funktionsfähige Platzhalter-Kontextmenüpunkte entfernt.

### Repository
- Installer-Ausgaben (`installer_out/`) werden nicht mehr versioniert.

## [1.0.0]

- Erste Version: Geräteerkennung, Auslesen, Dekodierung, Plot, Analyse-Werkzeuge,
  Chart-Stile, Einheiten, Speichern/Laden/Import/CSV-Export.
