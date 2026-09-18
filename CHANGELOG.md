# Änderungsverlauf

Format angelehnt an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/).

## [2.0.0]

Vollständige Überarbeitung. Auslöser war ein Befund an echten Gerätedaten: Bei jedem
Auslesevorgang konnte die zuletzt aufgezeichnete Aufnahme stillschweigend verlorengehen.

### Behoben: Datenverlust und falsche Messwerte

- **Die neueste Aufnahme ging beim Auslesen verloren.** Das Gerät meldet zu Beginn eine
  Adressspanne, sendet aber regelmäßig mehr Daten als darin angekündigt, an einem Gerät
  gemessen zwischen 18 und 500 Byte. Die Übertragung wurde beim Erreichen der angekündigten
  Menge beendet und der Rest abgeschnitten. Fiel dabei die Abschlusskennung einer Aufnahme
  weg, verschwand diese kommentarlos aus der Liste. Das Ende der Übertragung wird jetzt daran
  erkannt, dass die Leitung ruhig wird; die angekündigte Menge dient nur noch der
  Fortschrittsanzeige.

- **Eine Gerätesuche konnte eine laufende Übertragung zerstören.** Jedes beliebige
  USB-Ereignis im System löste eine Suche aus, die auch den gerade lesenden Anschluss öffnen
  wollte. Das schlug fehl, woraufhin der Anschluss als entfernt galt und die Verbindung samt
  bereits gelesener Daten verworfen wurde, ohne Meldung. Anschlüsse, die gerade gelesen
  werden, sind jetzt von der Suche ausgenommen.

- **Endtemperatur und Enddruck waren unskaliert.** Sie wurden ohne die Umrechnung übernommen,
  die für die Startwerte angewandt wurde: 787 statt 28,7 °C, 9824 statt 982,4 hPa. In der
  Oberfläche waren die Felder nicht sichtbar, in jeder gespeicherten Datei standen sie
  trotzdem. Beim Laden alter Dateien werden die Werte jetzt zurückgerechnet.

- **Der Trennmarker zwischen Aufnahmen wurde ohne Rasterprüfung gesucht.** Ein Bitmuster, das
  zufällig über zwei benachbarte Messfelder hinweg entsteht, konnte eine Aufnahme mitten
  auseinanderschneiden; die Bruchstücke wurden als neue Aufnahmen fehlgedeutet. Der Dekoder
  läuft jetzt strikt auf dem Raster des Protokolls.

- **Die Dekodierung konnte den gesamten Auslesevorgang mit einer Ausnahme beenden.** Fehlende
  Absicherung beim Auswerten von Zahlen führte dazu, dass ein einzelner unerwarteter Wert die
  Verarbeitung aller Aufnahmen abbrach. Unplausible Werte werden jetzt gemeldet statt geworfen.

### Hinzugefügt: Live mitschreiben

Schaltfläche unter „“ und „“. Der Logger wird im Sekundentakt nach Druck, Temperatur
und Beschleunigung gefragt; das Ergebnis ist eine **gewöhnliche Aufnahme**. Sie steht in der
Liste, lässt sich messen, glatten, exportieren und speichern wie jede andere.

Gespeichert werden die **unveränderten Antworten des Geräts** auf `D` und `M`, je Abfragetakt
eine Zeile mit der Zeit seit Beginn. Keine ausgewerteten Werte. Das ist dieselbe Regel wie beim
Auslesen des Speichers: Die Auswertung geschieht beim Öffnen und benutzt dieselben
Zerlegungsfunktionen wie die Live-Anzeige. Wird dort später ein Fehler behoben, zeigt eine alte
Datei danach die berichtigten Werte.

Zwei Dinge, die dabei nötig waren:

- **Das Abtastintervall ist jetzt eine Eigenschaft der Aufnahme.** Aus dem Gerätespeicher kommen
  4 Hz, live wird im Sekundentakt gemessen. Stand dort fest 0,25 s, wäre jede abgeleitete
  Sinkrate einer Live-Aufnahme um den Faktor des Taktverhältnisses falsch gewesen.

- **Maßgeblich ist der gemessene Takt, nicht der eingestellte.** Das Gerät antwortet, wann es
  antwortet; als Zeitstempel gilt die tatsächliche Uhrzeit der Abfrage.

Am Gerät nachgewiesen: mitgeschrieben, gespeichert, zurückgelesen, jeder Messwert identisch.

### Geändert: Umrechnung der Batteriespannung berichtigt

Die bisherige Umrechnung war **geraten und falsch**. Angabe des Herstellers: Der Rohwert stammt
aus einem 10-Bit-Umsetzer mit 2,5 V Referenz und einem Spannungsteiler 1:2, also

    U = Rohwert / 1024 * 2,5 V * 2 = Rohwert * 5 / 1024

Für den Rohwert 619 ergibt das **3,02 V** statt der bisher angezeigten 4,84 V. Das sind 1,01 V
je Zelle bei drei Zellen in Reihe; eine AG13 gilt unter 1,1 V als erschöpft. Der Ladezustand
stand damit auf 100 %, obwohl die Batterie leer war. Das erklärt nachträglich, warum das
Gerät Uhrzeit und Speicherinhalt beim Abziehen verliert.

Der Faktor gilt nicht mehr als vorläufig.

Angezeigt wird der Wert trotzdem nicht mehr. Er wird hinter dem Spannungswandler abgegriffen
und bleibt deshalb weitgehend konstant, solange der Wandler überhaupt arbeitet. Als Aussage
über den Ladezustand taugt er nicht. Belastbar ist allein das Statusbit, das das Gerät selbst
setzt; die Übersicht zeigt jetzt nur noch dieses.

### Geändert: Fehlendes Datum wird als solches ausgewiesen

Ist die Uhr des Geräts nicht gestellt, liefert es ein Datum aus Nullen. Fassung 1 setzte dann
den **01.01.1900** ein. Das sah wie eine Angabe aus, war aber keine. Solche Aufnahmen tragen
jetzt gar kein Datum, und die Übersicht sagt, woran es liegt.

Der Zeitstempel selbst ist gemischt kodiert: die Uhrzeit hexadezimal, das Datum dezimal. Dass
diese ungewöhnliche Aufteilung stimmt, belegt ein Auszug vom 17.09.2026, der an der
Datumsstelle `17092026` trägt. Hexadezimal gelesen ergäbe das den 23., einen Tag, den es beim
Auslesen noch nicht gab. Die Kodierung ist jetzt durch Tests an echten Gerätedaten festgehalten.

Die Uhrzeit bleibt in diesen Fällen gültig, zählt aber ab dem letzten Einschalten des Geräts
und nicht ab Mitternacht. Ursache ist die leere Batterie: Das Gerät verliert die Uhr, sobald
es ohne Strom ist.

### Hinzugefügt: Höhenbezug auf den Aufnahmebeginn

Neben „“ lässt sich jetzt auch der **erste** gemessene Druck als Nullpunkt
wählen. Sinnvoll, wenn der Logger am Absetzpunkt eingeschaltet wurde oder die Aufnahme nicht
bis zur Landung reicht und der Enddruck deshalb kein Bodendruck ist.

Dabei nachgemessen: Ein anderer Bezugsdruck verschiebt den Nullpunkt **und verändert den
Maßstab**, weil die barometrische Formel nicht linear ist. Zwischen 1013,25 und 1000,0 hPa
unterscheidet sich eine Höhendifferenz von 2900 m um 7,3 m, also 0,25 Prozent.

### Geändert: Geräte erscheinen erst mit Namen

Ein Gerät kommt erst in die Liste, wenn Modell und Seriennummer vorliegen. Ein Eintrag, der nur
„“ heißt und sich Sekunden später in „“ verwandelt, sieht aus wie ein Fehler,
und solange die Kopfdaten fehlen, ist gar nicht gesichert, dass dort ein Logger hängt.

### Hinzugefügt: Momentanwerte des Loggers

Menü „“, „“. Fragt im Sekundentakt Druck und Temperatur (Befehl `D`)
sowie die Beschleunigung aller drei Achsen (Befehl `M`) ab und rechnet daraus die Höhe im
eingestellten Bezug. Nützlich zum Prüfen eines Geräts vor dem Sprung, ohne etwas aufzeichnen zu
müssen. Der Anschluss bleibt währenddessen belegt; überlappende Abfragen sind ausgeschlossen.

### Hinzugefügt: Wählbares Glättungsverfahren

Fünf Verfahren statt eines, voreingestellt Savitzky-Golay. Das gewählte Verfahren wird in der
Oberfläche mit zwei Sätzen erklärt, weil es abgelesene Spitzenwerte verändert.

- **Savitzky-Golay** legt in jedes Fenster eine Parabel nach kleinsten Quadraten. Polynome bis
  Grad 2 gehen exakt durch, Höhe und Lage von Spitzen bleiben erhalten.
- **Gleitender Mittelwert** ist ein Rechteckfenster: Nebenzipfel bei 13 dB Dämpfung, drückt
  Spitzen systematisch ab.
- **Gauß** hat keinen Nebenzipfel und kein Überschwingen. Die Standardabweichung ist so gewählt,
  dass die Rauschunterdrückung der des Mittelwerts gleicher Fensterbreite entspricht.
- **Median** ist nichtlinear: Einzelne Ausreißer verschwinden vollständig, Sprünge bleiben scharf.
- **Glättender Spline** nach Whittaker und Henderson arbeitet nicht mit einem Fenster, sondern
  legt eine Kurve durch die gesamte Messreihe. Sie minimiert die Summe aus Abweichung und
  Krümmung. Geraden gehen exakt durch, eine gleichmäßige Sinkrate wird also nicht verfälscht,
  gleich wie stark geglättet wird. In der Mitte der Reihe glättet er bei gleicher
  Fensterangabe am stärksten von allen fünf, am Rand am schwächsten.

Alle fünf sind phasenfrei, verschieben also nichts in der Zeit. Das ist durch Tests belegt.

### Behoben: Der gleitende Mittelwert erfand Werte

Wo nichts gemessen wurde, bildete er aus den Nachbarwerten trotzdem einen Wert. Bei der
Temperatur, die erst ab dem ersten Einschub vorliegt, entstand dadurch am Anfang jeder Aufnahme
eine Kurve aus dem Nichts.

### Behoben: Die Sinkrate wurde nie geglättet

Sie war von der Glättung ausgenommen, ohne dass das irgendwo stand. Die Einstellung wirkte auf
alle Kurven außer der, bei der sie am meisten bringt.

### Behoben: Rückschritte gegenüber Fassung 1 in der Diagrammansicht

Im Betrieb gemeldet. Alles davon war in Fassung 1 vorhanden und in der Überarbeitung verloren
gegangen oder verschlechtert worden:

- **Die Achsen trugen nicht die Farbe ihrer Kurve.** Die Einfärbung fand statt, wurde aber
  unmittelbar danach von der Stilgebung wieder überschrieben, die mit `Axes.Color(...)` alle
  Achsen einheitlich setzt. Die Einfärbung steht jetzt hinter der Stilgebung.

- **Die Beschleunigungsachse erschien, obwohl die Kurve ausgeblendet war.** Sinkraten- und
  Beschleunigungsachse wurden unbedingt angelegt. Eine Skala ohne zugehörige Kurve ist
  schlimmer als keine. Sie legt nahe, dass dort etwas zu sehen wäre. Achsen entstehen jetzt
  nur für Kurven, die auch gezeichnet werden.

- **Die Zeitachse zeigte Sekunden seit Aufnahmebeginn statt der Uhrzeit.** Sie zeigt wieder die
  Uhrzeit des Geräts; gerechnet wird intern unverändert in Sekunden. Fadenkreuz, Messbereich
  und Marker geben die Zeit ebenfalls wieder als Uhrzeit an.

- **Marker gingen nur auf die Höhenkurve und ließen sich nicht einzeln entfernen.** Jede
  sichtbare Kurve nimmt jetzt Marker an, in ihrer Farbe, mit ihrer Einheit, auf ihrer Achse.
  Getroffen wird die Kurve, die dem Zeiger am nächsten liegt, gemessen in Bildpunkten. Ein
  Klick auf einen vorhandenen Marker entfernt ihn wieder.

- **Die Werteanzeige unter dem Mauszeiger galt nur für die Höhe.** Sie nahm die erste sichtbare
  Kurve, praktisch also immer die Höhe. Jetzt steht zu jeder gezeichneten Kurve ihr Wert da,
  jede mit einem Punkt in ihrer Farbe.

- **Beim Messen änderte sich der Mauszeiger nicht.** Über einer Bereichskante wird er jetzt zum
  waagerechten Verschiebezeiger; es war vorher nicht erkennbar, dass die Kanten greifbar sind.

- **Die Aufnahmeliste nannte nur die Startzeit.** Erste Zeile jetzt Gerät, Seriennummer und Tag,
  zweite Zeile die Zeitspanne von–bis, dazu wie bisher Dauer und Kennzahlen. Der Diagrammtitel
  trägt dieselben Angaben. Ohne sie ließ sich eine weitergegebene Auswertung nicht zuordnen.

- **Der ausgewählte Logger war nicht bedienbar, ohne vorher irgendwo hinzuklicken.** Er war
  ausgewählt, aber „“ und „“ blieben gesperrt: Die Befehle hängen an
  `CommandManager.RequerySuggested`, und das wird nur durch Eingaben ausgelöst, nach einem
  Suchlauf im Hintergrund also gar nicht. Die Befehle werden jetzt nach jedem Geräte- und
  Auslesevorgang neu bewertet.

### Behoben: Der Logger verschwand sporadisch aus der Geräteliste

Im Betrieb aufgefallen und im Protokoll belegt: Der Logger fiel ohne Zutun aus der Liste und
kam Sekunden später zurück.

```
09:29:14  Auslesen fertig
09:29:33  [WARN] Der Logger an COM3 ist nicht mehr erreichbar.
09:29:40  [INFO] Logger an COM3 erkannt.
```

Drei Ursachen, die zusammenwirkten:

- **Ein einzelner ausgebliebener Ping galt als Beweis.** Er ist keiner. Das Gerät verbucht jedes
  Öffnen und Schließen des Anschlusses als An- und Abmeldung und ist danach kurz nicht
  ansprechbar. Ein bekanntes Gerät muss jetzt **zweimal in Folge** ausbleiben, bevor es als
  entfernt gilt.

- **Die Kopfdaten wurden bei jedem Suchlauf neu gelesen.** Sie ändern sich nie. Dafür wurde der
  Anschluss ein zweites Mal geöffnet und geschlossen. Der Suchlauf hat sich damit selbst
  gestört und die Betriebshistorie des Geräts mit Ereignissen gefüllt. Sie werden jetzt einmal
  je Gerät geholt.

- **Ein nicht zu öffnender Anschluss galt als „“.** Windows gibt einen seriellen
  Anschluss nach dem Schließen nicht augenblicklich frei. Das Öffnen wird jetzt nach kurzer
  Wartezeit einmal wiederholt, und ein gescheitertes Öffnen wird nicht mehr als Urteil über das
  Gerät behandelt.

Außerdem wird die Geräteliste **nachgeführt statt neu aufgebaut**. Das vorherige Leeren mit
anschließendem Neuaufbau ließ die Auswahl springen und die Anzeige flackern, auch wenn sich
nichts geändert hatte.

### Behoben: Anmeldung am Gerät

- **Die Anmeldung beim Logger fand nur während der Gerätesuche statt.** Das Gerät nimmt erst
  Befehle an, nachdem es auf ein `*` mit `?` geantwortet hat. Diese Anmeldung wurde beim
  Suchlauf erledigt und danach als dauerhaft gültig angenommen. Sie ist es nicht: Die
  USB-Schnittstelle sitzt im **Adapter**, nicht im Logger. Zieht man den Logger vom Adapter ab
  und steckt ihn wieder auf, bleibt der Anschluss am Rechner bestehen, es entsteht kein
  Geräteereignis, aber die Anmeldung ist verfallen. Jeder folgende Zugriff lief ins Leere.
  Die Verbindung meldet sich jetzt selbst an und wiederholt das bei Bedarf, ohne dass der
  Anwender etwas tun muss.

- **Der Befehl `B` machte die Anmeldung ungültig, ohne dass das vermerkt wurde.** Nach dem
  ordentlichen Beenden der Kommunikation schlug der nächste Zugriff fehl.

- **Ein abgemeldetes Gerät schweigt nicht.** Am Gerät gemessen: Nach `B` beantwortet es ein
  `L` zuerst mit `?` und danach nur noch mit dessen Echo. Eine erste Fassung der
  Wiederanmeldung prüfte auf Stille und griff deshalb nie. Maßgeblich ist jetzt, ob überhaupt
  Nutzlast zurückkommt.

- **Die Gerätesuche läuft nach einem stummen Gerät automatisch erneut**, damit die Liste nicht
  weiter einen Logger anzeigt, der nicht mehr am Adapter steckt. Von allein kann sie das nicht
  bemerken. Es gibt kein Ereignis, an dem es sich erkennen ließe.

- **Fehlt der Adapter, stand dort bisher eine rohe Systemmeldung** über einen „“.
  Jetzt steht da, dass der USB-Adapter nicht am Rechner steckt. Ein belegter Anschluss wird
  ebenfalls als solcher benannt.

### Behoben: „“ tat etwas anderes

- Der Menüpunkt hat die **gesamte Auslesung** gespeichert. Die Einschränkung auf die
  ausgewählte Aufnahme wurde beim Schreiben verworfen, weil die Datei grundsätzlich den
  Rohdatenstrom ablegt. Wer die Datei wieder öffnete, hatte alle Aufnahmen vor sich, und die
  Dateigröße entsprach dem vollen Speicherauszug. Der Punkt schneidet jetzt tatsächlich zu.

### Hinzugefügt: Einzelne Aufnahme weitergeben

- **Eine Aufnahme lässt sich als eigene Datei speichern** (Rechtsklick auf die Aufnahme).
  Enthalten ist der **unveränderte Rohausschnitt** dieser Aufnahme, zeichengenau aus dem
  Auslesevorgang herausgeschnitten, keine ausgerechneten Werte. Das ist der entscheidende
  Punkt: Wird ein Fehler in der Auswertung behoben, zeigt dieselbe Datei danach die
  berichtigten Werte. Genau daran ist die Programmversion 1 gescheitert, was einmal falsch
  dekodiert gespeichert war, blieb falsch.

- **Gerätekopf und Betriebsereignisse werden mitgegeben.** In einem gemessenen Auszug sind das
  848 von 105772 Hexzeichen, also 0,8 %. Enthalten ist darin aber die gesamte Betriebshistorie
  des Geräts samt Batteriewarnungen. Ohne sie ließe sich später nicht mehr beantworten, in
  welchem Zustand das Gerät beim Sprung war.

- **Herkunftsnachweis.** Jede Einzelaufnahme-Datei vermerkt Name und Prüfsumme des
  Auslesevorgangs, aus dem sie geschnitten wurde, sowie die genaue Stelle. Beide Bestandteile,
  Ausschnitt und Umfeld, sind einzeln prüfsummengesichert; eine nachträgliche Änderung wird
  beim Öffnen gemeldet.

- Beim Öffnen weist das Programm darauf hin, dass eine solche Datei **nicht** den gesamten
  Gerätespeicher enthält. Eine einzelne Aufnahme darf nicht mit einem vollständigen Auszug
  verwechselt werden.

- Aufnahmen aus älteren Dateien, die keinen Rohausschnitt mitbringen, lassen sich **nicht**
  einzeln speichern. Das Programm sagt das und bietet nicht ersatzweise an, ausgewertete Werte
  als Rohdaten auszugeben.

### Hinzugefügt: Gerätebedienung

- **Die Uhr des Loggers lässt sich stellen.** Menü „“, „“. Das
  Fenster stellt Loggerzeit, Rechnerzeit und die Abweichung gegenüber und setzt den Befehl
  erst im Augenblick des nächsten vollen Minutenwechsels ab. Das ist kein Schönheitsfehler,
  sondern nötig: Das Gerät setzt die Sekunden beim Stellen selbst auf null, mitten in einer
  Minute abgesetzt ginge die Loggeruhr anschließend um bis zu 59 Sekunden vor. Anschließend
  wird die Uhr zurückgelesen und der erreichte Stand angezeigt. Am Gerät nachgewiesen:
  Abweichung 0,0 s zwischen gesendetem und zurückgelesenem Zeitpunkt.

- **Momentanwerte und Kenndaten des Geräts** werden ausgewertet: Temperatur und Druck,
  Beschleunigung aller drei Achsen, Gerätekennung und Anzahl der Speicherumläufe.

- **Die Kommunikation wird ordentlich beendet**, statt die Schnittstelle einfach zu schließen.

- Nicht angebunden bleiben die Befehle zum Zurücksetzen der Fehlermerker und zur Korrektur
  des Druckwerts. Beide verändern den Zustand des Geräts; ihre Wirkung ist nicht vollständig
  dokumentiert und gehört nicht in eine Auswertesoftware.

### Hinzugefügt: Auswertung

- **Einstellbarer Höhenbezug.** Bisher wurde fest gegen die Normatmosphäre mit 1013,25 hPa
  gerechnet; bei einem Tagesluftdruck von 985 hPa liegt die so berechnete Höhe über 200 m
  daneben. Vier Einstellungen stehen zur Wahl, voreingestellt ist die Höhe über dem
  Landepunkt: Sie nutzt den gemessenen Enddruck als Nullpunkt, braucht keine Eingabe und ist
  gegen einen Fehler im angenommenen Tagesluftdruck weitgehend unempfindlich.
  Der gewählte Bezug steht dauerhaft über dem Diagramm, im Diagrammtitel und in jeder
  exportierten Datei.

- **Sprungphasen und Kennzahlen.** Absprung, Öffnung und Landung werden erkannt und im
  Diagramm markiert; Absprunghöhe, Freifallzeit, Öffnungshöhe, Sinkraten und
  Spitzenbeschleunigung stehen ohne weitere Bedienung in der Übersicht. Liegt kein
  eindeutiges Sprungprofil vor, wird das gesagt, statt Werte zu erfinden.

- **Vertikalgeschwindigkeit als eigene Kurve**, berechnet über eine gleitende lineare
  Regression statt als Differenz benachbarter Werte. Das senkt das Rauschen um etwa den
  Faktor 11.

- **Betriebshistorie des Geräts.** Der Logger speichert jedes Ein- und Ausschalten mit
  Zeitstempel, Temperatur und Druck. Diese Sätze wurden bisher überlesen; sie werden jetzt
  ausgewertet, angezeigt und mit archiviert.

- **Sättigung des Beschleunigungssensors** wird erkannt und gekennzeichnet, im Diagramm, in
  der Kennzahlenübersicht und als eigene Spalte im CSV. „“ und „“ sind für
  eine Materialbeurteilung nicht dasselbe.

- **Ungestellte Loggeruhr** wird als solche ausgewiesen. Bisher erschien in diesem Fall der
  1. Januar 1900 als Datum.

### Geändert: Oberfläche

- Neu gestaltet: Gerätebereich, Diagramm mit dauerhaft sichtbarem Höhenbezug und
  Kennzahlenübersicht. Die Aufnahmeliste zeigt Dauer, Höhe und Freifallzeit je Eintrag.
- Voreingestellt sind drei Kurven statt sechs. Sechs gleichzeitig kann niemand lesen.
- Abbrechen ist jederzeit möglich; der Fortschritt nennt die geschätzte Restzeit.
  Nach einem Abbruch sendet das Gerät den Rest noch zu Ende, das wird angezeigt, statt den
  Logger für die nächsten Sekunden tot erscheinen zu lassen.
- Meldungen erscheinen in einer Liste statt in modalen Fenstern, und in verständlicher
  Sprache. Die frühere Endmeldung eines erfolgreichen Auslesevorgangs lautete „“.
- Oberfläche durchgehend deutsch. Tastaturbefehle ergänzt.
- Einstellungen bleiben erhalten: Einheiten, Geschwindigkeitseinheit, Höhenbezug,
  Fenstergröße. Bisher gingen sie bei jedem Start verloren.
- Kontraste auf WCAG AA gebracht. Die frühere Hilfstextfarbe erreichte 2,9:1 und trug
  ausgerechnet die Einheitenangaben.
- Bildexport des Diagramms und Zurücksetzen der Ansicht sind sichtbar. Beides gab es vorher
  schon, aber nur versteckt im Kontextmenü der Diagrammbibliothek.

### Geändert: Bezeichnungen

- Die Schaltfläche „“ heißt jetzt **„“**. Der zugrundeliegende Gerätebefehl liefert
  nicht die letzte Aufnahme, sondern **alles, was seit dem letzten Auslesevorgang
  aufgezeichnet wurde**, bei mehreren neuen Sprüngen also alle, bei keinem neuen nichts. Die
  alte Beschriftung war schlicht falsch und konnte dazu verleiten, Sprünge für verloren zu
  halten.

### Geändert: Gerätesuche

- Anschlüsse werden über ihre USB-Kennung vorgefiltert. Bisher wurde in jeden COM-Port des
  Rechners ein Zeichen geschrieben. Das kann fremde Geräte stören: Bei Messtechnik mit
  SCPI-Befehlssatz ist genau dieses Zeichen das Präfix der Standardbefehle, Mikrocontroller
  starten beim Öffnen des Anschlusses neu, und Bluetooth-Anschlüsse blockieren sekundenlang.
  Das Durchsuchen aller Anschlüsse bleibt als ausdrücklich zu wählende Möglichkeit erhalten.

### Geändert: Dateien

- **Neues Archivformat.** `.sdvlog` enthält jetzt die **Rohdaten** samt SHA-256-Prüfsumme,
  nicht mehr nur das Auswertungsergebnis. Damit lassen sich künftige Korrekturen an der
  Auswertung rückwirkend auf archivierte Aufzeichnungen anwenden, und eine nachträgliche
  Veränderung der Messwerte wird erkannt. Dateien der Fassung 1 werden weiterhin gelesen.
- **Jeder Auslesevorgang wird sofort als Rohdatei gesichert**, vor jeder Auswertung. Ein
  Auswertungsfehler kann eine Übertragung nicht mehr vernichten.
- **CSV mit festgelegtem Format.** Bisher hingen Dezimal- und Feldtrennzeichen von den
  Einstellungen des exportierenden Rechners ab und standen nirgends in der Datei; zwischen
  zwei Standorten ausgetauscht war keine Datei zuverlässig lesbar. Jetzt stehen zwei
  benannte Formate zur Wahl, und Gerät, Höhenbezug und Einheiten stehen im Dateikopf.

### Geändert: Technik

- Umstellung auf .NET 10 (unterstützt bis November 2028), SDK-Projektdateien, drei Projekte
  statt einer 5205 Zeilen langen Datei.
- Der Fachkern ist oberflächenfrei und durch 36 Tests gegen echte Gerätedaten belegt, dazu
  7 Tests gegen angeschlossene Hardware.
- Die Abhängigkeit FontAwesome.WPF entfällt. Das Paket stammt aus dem Jahr 2017, liefert nur
  eine Fassung für .NET Framework 4.0 und hätte jede Aktualisierung blockiert. Ersetzt durch
  eigene Vektorsymbole.
- Selbsttest über die Befehlszeile für Kundendienst und Bauprüfung.

### Geändert: Auslieferung

- Installation **ohne Administratorrechte**, wahlweise für alle Benutzer.
- Die Lizenzbedingungen werden im Setup angezeigt und die Zustimmung eingeholt. Bisher wurde
  überhaupt kein Rechtstext ausgeliefert.
- Der Dateiname der Anwendung enthält keine Versionsnummer mehr. Bisher blieben bei einem
  Update zwei Programmdateien nebeneinander liegen, und Verknüpfungen zeigten auf die alte.
- Ausgeliefert wird der Inhalt von `publish/`, nicht der von `bin/Release/`. Dort sammelten
  sich Reste früherer Baustände an, darunter Bibliotheken für Linux und macOS.
- Deutsche Bedienungsanleitung und ein Dokument zu Messgenauigkeit und Grenzen gehören zum
  Lieferumfang.

### Bekannt und offen

- Das Setup ist **nicht signiert**. Solange kein Zertifikat vorliegt, zeigt Windows beim
  Start die Warnung „“.
- Die Lizenzbedingungen sind ein Entwurf und anwaltlich zu prüfen.
- Die Prüfsumme im Gerätekopf wird angezeigt, aber nicht zur Prüfung des übertragenen
  Datenstroms verwendet. Über welchen Bereich und mit welchem Verfahren sie gebildet wird,
  ist nicht dokumentiert.
- Herstellerseitige Genauigkeitsangaben zum Drucksensor und zur Zeitbasis fehlen in der
  Dokumentation noch.
- Die Beschleunigungsachsen sind nicht abgeglichen: Ein ruhendes Gerät meldet je nach Lage
  einen Betrag zwischen etwa 0,87 g und 0,98 g statt durchgehend 1,00 g. Für die Beurteilung
  von Freifall und Öffnungsstoß ist das unerheblich, für eine absolute Angabe in g nicht.

---

## [1.0.0]

Erste Fassung: Geräteerkennung, Auslesen, Dekodierung, Diagramm, Analysewerkzeuge,
Diagrammgestaltung, Einheiten, Speichern, Laden, Rohdatenimport und CSV-Ausgabe.
Der Quellstand liegt unter `legacy/v1/`.
