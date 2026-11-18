using SolidEdgeCommunity;
using SolidEdgeDraft;

using SeApplication = SolidEdgeFramework.Application;
using SeDocument = SolidEdgeFramework.SolidEdgeDocument;

namespace SE_RenderDrawing;

/// <summary>
/// Solid Edge Makro: neue Revision in die Änderungstabelle der aktiven Zeichnung eintragen,
/// Zeichnung speichern und als PDF im Unterordner "Zeichnungen" ablegen.
///
/// Aufruf ohne Argumente (z. B. als Makro aus Solid Edge) zeigt den Dialog.
/// Optionale Argumente (für Tests / Stapelbetrieb):
///   --doc &lt;pfad.dft&gt;   bereits geöffnete Zeichnung statt der aktiven verwenden
///   --table &lt;n&gt;        Tabelle n (1…) statt automatischer Auswahl (0)
///   --rev &lt;XX&gt;  --text &lt;Beschreibung&gt;  --name &lt;Kürzel&gt;  --date &lt;TT.MM.JJJJ&gt;
///   --no-dialog         ohne Dialog
///   --update            Zeichnungsansichten aktualisieren
///   --clear-tracker     Hinweise geänderter Bemaßungen löschen
///   --black / --no-black  PDF mit / ohne „Alle Farben schwarz speichern“
///   --model &lt;x_t|jt|stp|igs|stl|pdf&gt;  zusätzlich das 3D-Modell exportieren; --no-model aus
///   --no-entry          nichts in die Tabelle eintragen (nur exportieren)
///   --force-open        3D-Export nicht im Hintergrund, sondern über das geöffnete Teil (Test)
///   --list              nur Tabellen und automatische Auswahl ausgeben, nichts ändern
///   --pdf-dir &lt;ordner&gt;  Speicherort der PDF (absolut oder relativ zur Zeichnung)
/// </summary>
internal static class Program
{
    private static string Title => Lang.L("App.Title");

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var cfg = MacroConfig.Load();
        Lang.Load(cfg.Language);
        Options opt;
        try { opt = Options.Parse(args); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        OleMessageFilter.Register();
        IWin32Window? owner = null;
        try
        {
            SeApplication app;
            try { app = SolidEdgeUtils.Connect(false); }
            catch { throw new InvalidOperationException(Lang.T("Err.SolidEdgeNotRunning")); }
            owner = new Win32Window(app.hWnd);
            return Run(app, cfg, opt, owner);
        }
        catch (Exception ex)
        {
            Report(opt, owner, Lang.T("Err.Prefix", ex.Message), MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            OleMessageFilter.Unregister();
        }
    }

    private static int Run(SeApplication app, MacroConfig cfg, Options opt, IWin32Window owner)
    {
        var doc = GetDraft(app, opt.DocPath);
        var draftPath = doc.FullName;
        if (string.IsNullOrEmpty(draftPath) || !Path.IsPathRooted(draftPath))
            throw new InvalidOperationException(Lang.T("Err.NotSaved"));
        if (doc.ReadOnly)
            throw new InvalidOperationException(Lang.T("Err.ReadOnly"));

        var tables = DraftTable.ReadAll(doc);
        var auto = DraftTable.AutoSelect(tables, cfg);

        if (opt.List)
        {
            Console.WriteLine($"Zeichnung: {draftPath}");
            foreach (var t in tables)
            {
                var map = t.GuessColumns(cfg);
                Console.WriteLine($"  {t.Number}: \"{t.Name}\" {t.ColumnCount} Spalten, {t.FilledCount}/{t.Capacity} belegt, " +
                                  $"Historie={t.IsRevisionHistory(cfg)}, jüngstes Datum={t.NewestDate():dd.MM.yyyy}, " +
                                  $"Spalten Rev/Beschr/Datum/Name={map[Field.Revision] + 1}/{map[Field.Description] + 1}/{map[Field.Date] + 1}/{map[Field.Editor] + 1}" +
                                  $"  Kopf: {string.Join(" | ", t.Headers)}");
            }
            Console.WriteLine($"  automatisch: {(auto is null ? "keine" : auto.Number.ToString())}");
            Console.WriteLine($"  Sprache: {Lang.Current} (verfügbar: {string.Join(", ", Lang.Available)}), Dialogtitel: \"{Lang.L("Dialog.Title")}\"");
            return 0;
        }

        DraftTable? table;
        Dictionary<Field, int> columns;
        string revision, text, editor, date, pdf;
        bool updateViews, clearTracker, allBlack, exportModel;
        ModelFormat modelFormat;
        var modelSource = ModelExport.FindModelPath(doc);

        if (opt.NoDialog)
        {
            table = opt.Table is > 0 ? tables.ElementAtOrDefault(opt.Table.Value - 1) ?? throw new ArgumentException(Lang.T("Err.TableMissing", opt.Table)) : auto;
            columns = table?.GuessColumns(cfg) ?? Enum.GetValues<Field>().ToDictionary(f => f, _ => -1);
            string? last = table is not null && columns[Field.Revision] >= 0 ? table.LastValue(columns[Field.Revision]) : null;
            revision = opt.Revision
                ?? (table is not null && columns[Field.Revision] >= 0 ? (last is null ? cfg.FirstRevision : RevisionCode.Next(last)) : null)
                ?? (cfg.PdfName.AppendRevision ? throw new ArgumentException(Lang.T("Err.RevisionUnknown")) : string.Empty);
            text = opt.Text ?? string.Empty;
            editor = opt.Editor ?? cfg.Editor
                ?? (table is not null && columns[Field.Editor] >= 0 ? table.LastValue(columns[Field.Editor]) : null)
                ?? Environment.UserName;
            date = opt.Date ?? DateTime.Today.ToString(cfg.DateFormat);
            updateViews = opt.UpdateViews ?? cfg.UpdateViews;
            clearTracker = opt.ClearTracker ?? cfg.ClearDimensionTracker;
            allBlack = opt.AllBlack ?? cfg.SaveAllColorsBlack ?? DrawingActions.GetSaveAllColorsBlack(app) ?? false;
            pdf = PdfNaming.FullPath(draftPath, revision, opt.PdfDir ?? cfg.PdfFolder, cfg.PdfName);
            modelFormat = opt.ModelFormat ?? cfg.ModelFormat;
            exportModel = (opt.ExportModel ?? cfg.ExportModel) && modelSource is not null;
        }
        else
        {
            using var dlg = new RevisionDialog(draftPath, tables, auto, cfg, opt.Table ?? 0,
                opt.Editor ?? cfg.Editor, opt.UpdateViews ?? cfg.UpdateViews, opt.ClearTracker ?? cfg.ClearDimensionTracker, opt.PdfDir ?? cfg.PdfFolder, cfg.PdfName,
                opt.AllBlack ?? cfg.SaveAllColorsBlack ?? DrawingActions.GetSaveAllColorsBlack(app) ?? false,
                modelSource, opt.ExportModel ?? cfg.ExportModel, opt.ModelFormat ?? cfg.ModelFormat, opt.Text);
            if (dlg.ShowDialog(owner) != DialogResult.OK)
                return 2;
            table = dlg.SelectedTable;
            columns = dlg.ColumnMap;
            revision = dlg.Revision; text = dlg.Description; editor = dlg.Editor; date = dlg.Date; pdf = dlg.PdfPath;
            cfg.PdfFolder = dlg.PdfFolder;
            cfg.PdfName = dlg.FileNameRule;
            updateViews = dlg.UpdateViews; clearTracker = dlg.ClearTracker; allBlack = dlg.SaveAllColorsBlack;
            cfg.SaveAllColorsBlack = allBlack;
            exportModel = dlg.ExportModel; modelFormat = dlg.ModelFormat;
            cfg.ExportModel = exportModel; cfg.ModelFormat = modelFormat;

            if (editor.Length > 0) cfg.Editor = editor;
            cfg.UpdateViews = updateViews;
            cfg.ClearDimensionTracker = clearTracker;
        }

        var log = new List<string>();
        if (cfg.LoadError is not null) log.Add(Lang.T("Err.ConfigLoad", cfg.LoadError));
        if (Lang.LoadError is not null) log.Add(Lang.LoadError);
        using (new WaitCursor())
        {
            if (updateViews)
            {
                int n = DrawingActions.UpdateViews(doc);
                log.Add(n > 0 ? Lang.T("Log.ViewsUpdated", n) : Lang.T("Log.ViewsUpToDate"));
            }
            if (clearTracker)
            {
                int n = DrawingActions.ClearDimensionTracker(doc);
                if (n > 0) log.Add(Lang.T("Log.TrackerCleared", n));
            }
            int detached = DrawingActions.CountDetachedDimensions(doc);
            if (detached > 0) log.Add(Lang.T("Log.Detached", detached));

            var values = new Dictionary<int, string>();
            void Put(Field f, string v) { if (columns.TryGetValue(f, out var c) && c >= 0) values[c] = v; }
            Put(Field.Revision, revision);
            Put(Field.Description, text);
            Put(Field.Date, date);
            Put(Field.Editor, editor);

            if (table is not null && values.Count > 0 && !opt.NoEntry)
            {
                var dropped = table.Add(values);
                log.Add(Lang.T("Log.RevisionEntered", revision, table.Number));
                if (dropped is not null)
                {
                    int rc = columns[Field.Revision];
                    log.Add(Lang.T("Log.OldestRemoved", rc >= 0 ? dropped[rc] : string.Join(" | ", dropped)));
                }
                doc.Save();
                log.Add(Lang.T("Log.Saved", Path.GetFileName(draftPath)));
            }
            else
            {
                log.Add(Lang.T("Log.NoTableEntry"));
                if (updateViews || clearTracker)
                {
                    doc.Save();
                    log.Add(Lang.T("Log.Saved", Path.GetFileName(draftPath)));
                }
            }

            // Ordner wird im Dialog nach Rückfrage angelegt, im Stapelbetrieb automatisch
            DrawingActions.ExportPdf(app, doc, pdf, allBlack);
            log.Add(Lang.T("Log.Pdf", pdf));

            if (exportModel)
            {
                var modelTarget = ModelExport.TargetPath(pdf, modelFormat, cfg.Model3DPdfSuffix);
                try
                {
                    var how = ModelExport.Export(app, doc, modelTarget, opt.ForceOpen);
                    log.Add(Lang.T("Log.Model", modelTarget, how));
                }
                catch (Exception ex) { log.Add(Lang.T("Log.ModelFailed", ex.Message)); }
            }
        }

        if (!opt.NoDialog && cfg.Save() is { } saveError) log.Add(Lang.T("Err.ConfigSave", saveError));
        Report(opt, owner, string.Join(Environment.NewLine, log), MessageBoxIcon.Information);
        return 0;
    }

    private static DraftDocument GetDraft(SeApplication app, string? path)
    {
        if (path is not null)
        {
            foreach (SeDocument d in app.Documents)
                if (string.Equals(d.FullName, path, StringComparison.OrdinalIgnoreCase))
                    return d as DraftDocument ?? throw new InvalidOperationException(Lang.T("Err.NotADraft", path));
            throw new InvalidOperationException(Lang.T("Err.DraftNotOpen", path));
        }
        if (app.Documents.Count == 0 || app.ActiveDocument is not DraftDocument draft)
            throw new InvalidOperationException(Lang.T("Err.NoActiveDraft"));
        return draft;
    }

    private static void Report(Options opt, IWin32Window? owner, string message, MessageBoxIcon icon)
    {
        if (opt.NoDialog || opt.List) Console.WriteLine(message);
        else if (owner is not null) MessageBox.Show(owner, message, Title, MessageBoxButtons.OK, icon);
        else MessageBox.Show(message, Title, MessageBoxButtons.OK, icon);
    }

    private sealed class Win32Window(int handle) : IWin32Window
    {
        public IntPtr Handle { get; } = new(handle);
    }

    private sealed class WaitCursor : IDisposable
    {
        private readonly Cursor _old = Cursor.Current ?? Cursors.Default;
        public WaitCursor() => Cursor.Current = Cursors.WaitCursor;
        public void Dispose() => Cursor.Current = _old;
    }

    private sealed class Options
    {
        public string? DocPath, Revision, Text, Editor, Date, PdfDir;
        public int? Table;
        public bool NoDialog, List;
        public bool? UpdateViews, ClearTracker, AllBlack, ExportModel;
        public ModelFormat? ModelFormat;
        public bool NoEntry, ForceOpen;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i]! : throw new ArgumentException(Lang.T("Err.MissingValue", args[i]));
                switch (args[i].ToLowerInvariant())
                {
                    case "--doc": o.DocPath = Next(); break;
                    case "--table": o.Table = int.Parse(Next()); break;
                    case "--rev": o.Revision = Next(); break;
                    case "--text": o.Text = Next(); break;
                    case "--name": o.Editor = Next(); break;
                    case "--date": o.Date = Next(); break;
                    case "--no-dialog": o.NoDialog = true; break;
                    case "--update": o.UpdateViews = true; break;
                    case "--clear-tracker": o.ClearTracker = true; break;
                    case "--black": o.AllBlack = true; break;
                    case "--no-black": o.AllBlack = false; break;
                    case "--model": o.ExportModel = true; o.ModelFormat = ParseFormat(Next()); break;
                    case "--no-model": o.ExportModel = false; break;
                    case "--no-entry": o.NoEntry = true; break;
                    case "--force-open": o.ForceOpen = true; break;
                    case "--list": o.List = true; break;
                    case "--pdf-dir": o.PdfDir = Next(); break;
                    default: throw new ArgumentException(Lang.T("Err.UnknownArgument", args[i]));
                }
            }
            return o;
        }

        private static ModelFormat ParseFormat(string s) => s.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "x_t" or "parasolid" => SE_RenderDrawing.ModelFormat.Parasolid,
            "jt" => SE_RenderDrawing.ModelFormat.JT,
            "stp" or "step" => SE_RenderDrawing.ModelFormat.STEP,
            "igs" or "iges" => SE_RenderDrawing.ModelFormat.IGES,
            "stl" => SE_RenderDrawing.ModelFormat.STL,
            "pdf" or "3dpdf" => SE_RenderDrawing.ModelFormat.Pdf3D,
            _ => throw new ArgumentException(Lang.T("Err.UnknownArgument", s)),
        };
    }
}
