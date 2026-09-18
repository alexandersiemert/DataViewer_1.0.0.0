# Altbestand Fassung 1

Dieser Ordner enthält den vollständigen Quellstand der Programmfassung 1, so wie er vor der
Überarbeitung auf Fassung 2 vorlag. Er wird **nicht mehr gebaut und nicht mehr gepflegt**.

Aufbewahrt wird er aus zwei Gründen:

1. **Nachvollziehbarkeit.** Auswertungen, die mit Fassung 1 erstellt wurden, lassen sich damit
   reproduzieren. Das ist wichtig, weil sich die Höhenrechnung geändert hat: Fassung 1 rechnete
   ausschließlich gegen die Normatmosphäre (1013,25 hPa), Fassung 2 voreingestellt gegen den
   Landepunkt.
2. **Protokollwissen.** Das Wissen über das Geräteprotokoll war nirgends dokumentiert, sondern
   steckte allein in `MainWindow.xaml.cs`. Es ist inzwischen in
   `src/DataViewer.Core/Protocol/` überführt und mit Tests gegen echte Gerätedaten belegt, aber
   der Ursprung bleibt hier nachlesbar.

Der Ordner kann gelöscht werden, sobald beides nicht mehr gebraucht wird; die Git-Historie
bewahrt den Stand ohnehin.
