# SE_RenderDrawing

Solid Edge Makro (C#, .NET 9) für die aktive Zeichnung (`.dft`):

1. **Dialog**
   - Oben die Vorschau der Änderungstabelle. Mit ◀ / ▶ lassen sich alle Tabellen der Zeichnung durchblättern.
     Die Mitte zeigt die Nummer der Tabelle in der Reihenfolge der Zeichnung; Position 0 heißt **automatisch**.
   - Felder **Revision**, **Beschreibung**, **Datum**, **Bearbeiter**, jeweils mit einer Auswahl
     **→ Spalte**, in welche Tabellenspalte der Wert geschrieben wird (oder „keine Spalte“).
   - Vorbelegung (live bei Wechsel von Tabelle bzw. Spalte, bleibt leer, wenn nicht ermittelbar):
     - Revision = letzter Eintrag der Revisionsspalte + 1 (`AA → AB … AZ → BA`, `01 → 02`), leere Tabelle → `FirstRevision`
     - Bearbeiter = zuletzt im Makro verwendet, sonst letzter Eintrag der Bearbeiterspalte
     - Datum = heute
   - **Speichern in:** Ordner der PDF, absolut (`D:\Ausgabe\`) oder relativ zum Ordner der Zeichnung
     (`.\Zeichnungen\`, Standard). Mit „…“ auswählbar. Darunter wird der vollständige PDF-Pfad angezeigt.
   - **Revision anhängen** (an/aus) mit vier Feldern, die nur bei gesetzter Option aktiv sind:
     **Trennzeichen** (`_`), **Anzahl** (`2`), **Position** (`vorstehend`/`nachstehend`), **Präfix/Suffix** (`-`).
     Die Revision wird beim n-ten Trennzeichen eingefügt (fehlt es: am Ende des Namens):
     - vorstehend: an den Teil davor angehängt, mit Präfix → `ABC_12-3456-789` + `-AC` + `_Bauteil`
     - nachstehend: vor den Teil danach gesetzt, mit Suffix → `ABC_12-3456-789_` + `AC-` + `Bauteil`
     - Option aus: PDF heißt wie die Zeichnung (`ABC_12-3456-789_Bauteil.pdf`)
   - Optional (standardmäßig **aus**): Zeichnungsansichten aktualisieren, Hinweise geänderter Bemaßungen löschen.
2. **Eintrag** in die Zeile nach dem letzten Eintrag. Ist die Tabelle voll, wird die älteste Zeile entfernt
   und alle übrigen rücken eine Zeile nach (die Anzeigerichtung, z. B. von unten nach oben, bestimmt die Vorlage).
3. **Speichern** der Zeichnung.
4. **PDF** im gewählten Ordner, mit den Standardwerten nach dem Muster `LIEFERANT_TEILENUMMER-XX_Bezeichnung.pdf`
   (z. B. `ABC_12-3456-789_Bauteil.dft` → `ABC_12-3456-789-AC_Bauteil.pdf`).
   Fehlt der Ordner, wird nach Rückfrage angelegt; liegt dort schon eine PDF gleichen Namens, wird vor dem Überschreiben gefragt.

Ohne passende Tabelle (z. B. Siemens-Standardvorlage ohne Tabellen) wird nach Rückfrage nur die PDF erzeugt;
die Revision wird dann nur für den Dateinamen verwendet. Abgehängte Bemaßungen werden nicht gelöscht, sondern gemeldet.

## Automatische Auswahl der Änderungstabelle

1. Tabellen, die wie eine Änderungshistorie aussehen (Spalten für Revision **und** Beschreibung erkennbar), sonst alle
2. davon bevorzugt Tabellen mit 4 Spalten
3. davon die mit dem jüngsten Datum in einer ihrer Zellen
4. bei Gleichstand die mit der höchsten Nummer

## Sprache

Alle Texte des Dialogs, der Rückfragen und der Ergebnismeldung stehen in `SE_RenderDrawing.lang.json`
neben der EXE. Jede Gruppe der obersten Ebene ist eine Sprache (`DE`, `EN`); gewählt wird sie mit
`Language` in der Konfiguration.

- Fehlt die eingestellte Sprache oder einzelne Texte darin, wird **Englisch** verwendet.
- Weitere Sprachen: eine Gruppe (z. B. `"FR": { … }`) mit den gewünschten Schlüsseln ergänzen –
  nicht übersetzte Schlüssel kommen aus `EN`. Platzhalter `{0}`, `{1}` … müssen erhalten bleiben, `\n` ist ein Zeilenumbruch.
- `_maxLength` legt für Beschriftungen die Höchstzahl an Zeichen fest; längere Übersetzungen werden am Ende abgeschnitten.
- Fehlt die Datei, wird sie mit DE und EN neu angelegt (die Standardtexte sind zusätzlich in der EXE eingebettet).
- Die Schaltflächen der Windows-Rückfragen (Ja/Nein) kommen von Windows und folgen der Systemsprache.

## Konfiguration

`SE_RenderDrawing.config.json` im Verzeichnis der EXE. Wird beim ersten Start mit Standardwerten angelegt
und nach jedem Lauf mit Dialog aktualisiert (Bearbeiter, Häkchen). Das Verzeichnis muss beschreibbar sein
(also nicht unter `C:\Program Files` ablegen), sonst wird nur eine Meldung angezeigt.

| Eintrag | Bedeutung |
|---|---|
| `Language` | Sprache des Dialogs, Gruppe in `SE_RenderDrawing.lang.json` (Standard `DE`, unbekannt → `EN`) |
| `Editor` | zuletzt verwendeter Bearbeiter |
| `UpdateViews` / `ClearDimensionTracker` | Vorbelegung der Häkchen (Standard `false`) |
| `DateFormat` | Datumsformat in der Tabelle (Standard `dd.MM.yyyy`) |
| `FirstRevision` | Revision bei leerer Tabelle (Standard `AA`) |
| `PdfFolder` | zuletzt verwendeter Speicherort, absolut oder relativ zur .dft (Standard `.\Zeichnungen\`) |
| `PdfName` | Revision im Dateinamen (zuletzt verwendet): `AppendRevision`, `Separator`, `Count`, `Position` (`Before`/`After`), `Affix` |
| `ColumnHeaders` | Spaltenköpfe je Feld (`Revision`, `Description`, `Date`, `Editor`), an denen die Spalten erkannt werden – erst exakter Treffer, dann „beginnt mit“ |

## Bauen

```bash
dotnet publish SE_RenderDrawing.csproj -c Release -r win-x64 --self-contained false -o publish
```

Voraussetzung auf dem Zielrechner: .NET 9 Desktop Runtime (x64).

## In Solid Edge einbinden

- Einmalig: **Extras → Makro ausführen** und `publish\SE_RenderDrawing.exe` wählen.
- Dauerhaft als Knopf: **Datei → Einstellungen → Anpassen** (Umgebung *Draft*), Kategorie **Makros**,
  `SE_RenderDrawing.exe` in die Schnellzugriffsleiste oder eine eigene Registerkarte ziehen.

## Kommandozeile (Tests / Stapel)

| Argument | Bedeutung |
|---|---|
| `--doc <pfad.dft>` | bereits geöffnete Zeichnung statt der aktiven |
| `--table <n>` | Tabelle n (1…) statt automatisch (0) |
| `--rev <XX>` / `--text <Text>` / `--name <Kürzel>` / `--date <TT.MM.JJJJ>` | Werte vorgeben |
| `--no-dialog` | ohne Dialog, Ausgabe auf stdout |
| `--update` / `--clear-tracker` | Ansichten aktualisieren / Bemaßungshinweise löschen |
| `--pdf-dir <ordner>` | Speicherort der PDF (absolut oder relativ zur Zeichnung); im Stapelbetrieb wird ein fehlender Ordner ohne Rückfrage angelegt |
| `--list` | nur Tabellen, Spaltenzuordnung und automatische Auswahl ausgeben, nichts ändern |

Exit-Code: `0` OK, `1` Fehler, `2` abgebrochen.

## Dateien

- `Program.cs` – Ablauf, Verbindung zu Solid Edge, Kommandozeile
- `RevisionDialog.cs` – Eingabedialog mit Tabellenumschaltung und Spaltenzuordnung
- `DraftTable.cs` – Tabellen lesen/schreiben, Spaltenerkennung, automatische Auswahl
- `RevisionCode.cs` – Revision hochzählen
- `PdfNaming.cs` – PDF-Dateiname und Zielordner
- `DrawingActions.cs` – Ansichten aktualisieren, Bemaßungshinweise, PDF-Export
- `MacroConfig.cs` – Konfigurationsdatei
- `Lang.cs`, `SE_RenderDrawing.lang.json` – Übersetzungen (DE, EN, erweiterbar)
