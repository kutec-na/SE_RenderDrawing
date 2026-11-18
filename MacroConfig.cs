using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SE_RenderDrawing;

/// <summary>Spalten der Änderungstabelle, die das Makro befüllt.</summary>
public enum Field { Revision, Description, Date, Editor }

/// <summary>Ob die Revision an den Teil vor oder nach dem n-ten Trennzeichen angefügt wird.</summary>
public enum RevisionPosition { Before, After }

/// <summary>
/// Regel, wie die Revision in den PDF-Dateinamen eingefügt wird.
/// Beispiel (Standard): "ABC_12-3456-789_Bauteil" mit Trennzeichen "_", Anzahl 2, vorstehend, "-"
/// → "ABC_12-3456-789-AC_Bauteil".
/// </summary>
public sealed class FileNameRule
{
    /// <summary>Revision überhaupt in den Dateinamen einfügen.</summary>
    public bool AppendRevision { get; set; } = true;
    /// <summary>Trennzeichen zwischen den Namensteilen.</summary>
    public string Separator { get; set; } = "_";
    /// <summary>Einfügestelle = beim n-ten Trennzeichen (fehlt es, am Ende des Namens).</summary>
    public int Count { get; set; } = 2;
    /// <summary>Before: an den Teil davor anhängen ("…105-AC_…"), After: vor den Teil danach setzen ("…105_AC-Sicht…").</summary>
    public RevisionPosition Position { get; set; } = RevisionPosition.Before;
    /// <summary>Verbindungszeichen: bei "vorstehend" vor der Revision (Präfix), bei "nachstehend" danach (Suffix).</summary>
    public string Affix { get; set; } = "-";

    public FileNameRule Clone() => (FileNameRule)MemberwiseClone();
}

/// <summary>
/// Einstellungen des Makros. Liegen als <c>SE_RenderDrawing.config.json</c> im Verzeichnis der EXE.
/// Fehlt die Datei, wird sie mit Standardwerten angelegt.
/// </summary>
public sealed class MacroConfig
{
    public const string FileName = "SE_RenderDrawing.config.json";

    /// <summary>Sprache des Dialogs = Gruppe in SE_RenderDrawing.lang.json ("DE", "EN", …); unbekannt → Englisch.</summary>
    public string Language { get; set; } = "DE";

    /// <summary>Zuletzt verwendeter Bearbeiter (Vorbelegung im Dialog).</summary>
    public string? Editor { get; set; }

    /// <summary>Zeichnungsansichten vor dem Eintragen aktualisieren (Vorbelegung im Dialog).</summary>
    public bool UpdateViews { get; set; } = false;

    /// <summary>Hinweise der Bemaßungsverfolgung löschen (Vorbelegung im Dialog).</summary>
    public bool ClearDimensionTracker { get; set; } = false;

    /// <summary>PDF mit „Alle Farben schwarz speichern“ erzeugen (zuletzt verwendet; null = Einstellung von Solid Edge übernehmen).</summary>
    public bool? SaveAllColorsBlack { get; set; }

    /// <summary>Zusätzlich das 3D-Modell exportieren (zuletzt verwendet).</summary>
    public bool ExportModel { get; set; } = false;

    /// <summary>Format des 3D-Exports (zuletzt verwendet).</summary>
    public ModelFormat ModelFormat { get; set; } = ModelFormat.STEP;

    /// <summary>Format, in dem das Datum in die Tabelle geschrieben wird.</summary>
    public string DateFormat { get; set; } = "dd.MM.yyyy";

    /// <summary>Revision, wenn die Tabelle noch leer ist.</summary>
    public string FirstRevision { get; set; } = "AA";

    /// <summary>Speicherort der PDF: absolut oder relativ zum Ordner der .dft (zuletzt verwendet).</summary>
    public string PdfFolder { get; set; } = @".\Zeichnungen\";

    /// <summary>Wie die Revision in den PDF-Dateinamen eingefügt wird (zuletzt verwendet).</summary>
    public FileNameRule PdfName { get; set; } = new();

    /// <summary>
    /// Spaltenköpfe, an denen die Spalten erkannt werden (Groß-/Kleinschreibung egal,
    /// zuerst exakter Treffer, dann "beginnt mit").
    /// </summary>
    public Dictionary<Field, string[]> ColumnHeaders { get; set; } = new()
    {
        [Field.Revision] = ["Index", "Rev", "Rev.", "Revision", "Änd.-Index", "Änderungsindex", "Version", "Ausgabe"],
        [Field.Description] = ["Änderung", "Beschreibung", "Änderungsbeschreibung", "Description", "Change", "Inhalt"],
        [Field.Date] = ["Datum", "Date", "Änd.-Datum"],
        [Field.Editor] = ["Name", "Bearbeiter", "Autor", "Author", "Ersteller", "Gezeichnet", "By"],
    };

    /// <summary>Fehlermeldung beim Lesen (Text der Ausnahme) oder null.</summary>
    [JsonIgnore]
    public string? LoadError { get; private set; }

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Umlaute lesbar speichern
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static MacroConfig Load()
    {
        MacroConfig cfg;
        string? error = null;
        try
        {
            cfg = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<MacroConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new()
                : new();
        }
        catch (Exception ex)
        {
            cfg = new();
            error = ex.Message;
        }
        // fehlende Einträge (z. B. neue Felder) mit Standardwerten ergänzen
        var defaults = new MacroConfig().ColumnHeaders;
        foreach (var f in Enum.GetValues<Field>())
            if (!cfg.ColumnHeaders.TryGetValue(f, out var v) || v is null || v.Length == 0)
                cfg.ColumnHeaders[f] = defaults[f];
        cfg.PdfName ??= new();
        cfg.LoadError = error;
        if (error is null && !File.Exists(FilePath)) cfg.Save();
        if (string.IsNullOrWhiteSpace(cfg.Language)) cfg.Language = "EN";
        return cfg;
    }

    /// <summary>Speichert die Konfiguration. Gibt bei Fehler (z. B. keine Schreibrechte) den Text der Ausnahme zurück.</summary>
    public string? Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
