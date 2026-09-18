import re, os, collections

root = r"C:\Users\siemerta\source\repos\DataViewer_1.0.0.0"
v1x = open(root + r"\legacy\v1\MainWindow.xaml", encoding="utf-8", errors="replace").read()
v1c = open(root + r"\legacy\v1\MainWindow.xaml.cs", encoding="utf-8", errors="replace").read()

v2 = ""
for f in ["Views\\MainWindow.xaml", "Views\\MainWindow.xaml.cs", "Views\\PlotController.cs",
          "Views\\PlotTools.cs", "ViewModels\\MainViewModel.cs", "Services\\AppServices.cs", "Services\\DeviceWatcher.cs",
          "Views\\AltitudeReferenceWindow.xaml", "Views\\AltitudeReferenceWindow.xaml.cs"]:
    v2 += open(root + r"\src\DataViewer.App\\" + f, encoding="utf-8", errors="replace").read()
for f in ["Protocol\\PayloadDecoder.cs", "Analysis\\JumpAnalyzer.cs", "Analysis\\Signal.cs",
          "Io\\CsvExporter.cs", "Io\\SdvLogFile.cs", "Device\\LoggerConnection.cs", "Device\\LoggerPort.cs"]:
    v2 += open(root + r"\src\DataViewer.Core\\" + f, encoding="utf-8", errors="replace").read()

# Menuestruktur der Fassung 1
menus = re.findall(r'<MenuItem Header="([^"]+)"', v1x)
# Beschriftete Bedienelemente
buttons = re.findall(r'<TextBlock Text="([^"]{2,24})" Margin="\d', v1x)
# Ereignisbehandlungen
handlers = sorted(set(re.findall(r'(?:Click|Checked|Unchecked|KeyDown|PreviewMouseDown)="(\w+)"', v1x)))

# Funktionsliste: Name -> Suchbegriffe in Fassung 2
FEATURES = [
    ("Geraetesuche automatisch (USB)",        ["Win32_DeviceChangeEvent"]),
    ("Geraetesuche manuell",                  ["RefreshDevicesAsync"]),
    ("Mehrere Geraete gleichzeitig",          ["ObservableCollection<DeviceItem>"]),
    ("Kopfdaten lesen (Modell/Serie/Datum)",  ["ReadInfoAsync"]),
    ("Neue Aufnahmen lesen (seit letztem Auslesen)", ["CommandReadNew"]),
    ("Gesamten Speicher lesen",               ["CommandReadAll"]),
    ("Fortschrittsanzeige beim Lesen",        ["ReadProgress"]),
    ("Auslesen abbrechen",                    ["CancelReadCommand"]),
    ("Aufnahmen im Baum/Liste waehlen",       ["SelectedRecording"]),
    ("Diagramm: Hoehe",                       ["SeriesKind.Altitude"]),
    ("Diagramm: Temperatur",                  ["SeriesKind.Temperature"]),
    ("Diagramm: Beschleunigung Betrag",       ["SeriesKind.AccMagnitude"]),
    ("Diagramm: Beschleunigung X/Y/Z",        ["SeriesKind.AccX"]),
    ("Diagramm: Vertikalgeschwindigkeit",     ["SeriesKind.Speed"]),
    ("Serien ein-/ausblenden",                ["OnSeriesToggled"]),
    ("Messcursor (Bereich)",                  ["PlotTool.Measure"]),
    ("Messcursor: Min/Max/Delta/Mittel",      ["RangeStatistics.Compute"]),
    ("Messcursor: Geschwindigkeit",           ["RatePerSecond"]),
    ("Messkanten nachziehbar",                ["EdgeUnderCursor"]),
    ("Fadenkreuz",                            ["PlotTool.Crosshair"]),
    ("Fadenkreuz-Ablesewerte",                ["CrosshairReadout"]),
    ("Marker",                                ["PlotTool.Marker"]),
    ("Legende ein/aus",                       ["ShowLegend"]),
    ("Hover-Tooltip am Messpunkt",            ["UpdateHover"]),
    ("Achsengrenzen von Hand",                ["SetAxisLimits"]),
    ("Glaettung je Serie",                    ["SmoothingFor"]),
    ("Einheiten metrisch/imperial",           ["UnitSystem.Imperial"]),
    ("Geschwindigkeitseinheit waehlbar",      ["SpeedUnit"]),
    ("Diagrammstil hell/dunkel/schiefer",     ["ChartStyle.Slate"]),
    ("Serienfarben frei waehlbar",            ["OnSeriesStyle"]),
    ("Linienbreite frei waehlbar",            ["st.Width = captured"]),
    ("Linienmuster frei waehlbar",            ["ParsePattern"]),
    ("Gitter ein/aus",                        ["ShowGrid"]),
    ("Achsenbeschriftungsfarben",             ["AxisLabelsFollowSeries"]),
    ("Datei oeffnen (.sdvlog)",               ["SdvLogFile.Load"]),
    ("Datei speichern (.sdvlog)",             ["SdvLogFile.Save"]),
    ("Alle Aufnahmen speichern",              ["Alle Aufnahmen speichern"]),
    ("Rohdaten importieren (.lgd)",           ["ImportRaw"]),
    ("CSV-Export",                            ["CsvExporter"]),
    ("CSV-Export je Aufnahme aus Kontextmenue", ["OnExportCsvContext"]),
    ("Diagramm als Bild speichern",           ["SavePng"]),
    ("Hilfe / Dokumentation",                 ["OnHelp"]),
    ("Ueber-Dialog",                          ["AboutWindow"]),
    ("Hoehenbezug einstellbar (QNH/Grund)",   ["AltitudeReference"]),
    ("Sprungphasen-Erkennung",                ["JumpAnalyzer"]),
    ("Kennzahlenuebersicht",                  ["JumpMetrics"]),
    ("Betriebshistorie (Ein/Aus)",            ["DeviceEventKind"]),
    ("Saettigungs-Kennzeichnung",             ["AccSaturated"]),
    ("Einstellungen bleiben erhalten",        ["AppSettings"]),
    ("Uhr des Loggers stellen",               ["ClockWindow"]),
    ("Loggeruhr auslesen",                    ["ReadClockAsync"]),
    ("Momentanwerte Temperatur/Druck",        ["ReadLiveAsync"]),
    ("Geraetekennung und Speicherumlaeufe",   ["ReadFactsAsync"]),
    ("Kommunikation ordentlich beenden",      ["EndCommunicationAsync"]),
    ("Einzelne Aufnahme als Rohausschnitt",   ["SaveRecording"]),
    ("Herkunftsnachweis am Ausschnitt",       ["ExcerptSourceSha256"]),
    ("Betriebshistorie am Ausschnitt",        ["RawContext"]),
]

print("=" * 74)
print("FUNKTIONSABGLEICH  Fassung 1  ->  Fassung 2")
print("=" * 74)
missing = []
for name, keys in FEATURES:
    hit = any(k in v2 for k in keys)
    in_v1 = any(k in v1x or k in v1c for k in keys) or True
    mark = "vorhanden" if hit else "FEHLT"
    if not hit:
        missing.append(name)
    print(f"  {mark:<10} {name}")

print()
print(f"Vorhanden: {len(FEATURES)-len(missing)} von {len(FEATURES)}")
print(f"Fehlt    : {len(missing)}")
for m in missing:
    print(f"   - {m}")

print()
print("=" * 74)
print("MENUESTRUKTUR FASSUNG 1 (zum Abgleich)")
print("=" * 74)
print(", ".join(dict.fromkeys(menus)))

