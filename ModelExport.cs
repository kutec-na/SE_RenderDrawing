using SolidEdgeDraft;
using SeApplication = SolidEdgeFramework.Application;
using SeDocument = SolidEdgeFramework.SolidEdgeDocument;

namespace SE_RenderDrawing;

/// <summary>
/// Austauschformate für das 3D-Modell.
/// (3D-PDF ist nicht enthalten: über die API liefert Solid Edge bei *.pdf nur den 2D-Übersetzer;
/// der Dateityp „3D Adobe Acrobat-Dokumente“ aus dem Dialog „Als übersetzt speichern“ ist nicht ansprechbar.)
/// </summary>
public enum ModelFormat { Parasolid, JT, STEP, IGES, STL }

/// <summary>
/// Exportiert das 3D-Modell, auf dem die Zeichnung beruht (Verknüpfung der ersten Hauptansicht).
/// Zuerst im Hintergrund über das von der Zeichnung geladene Modell; geht das nicht,
/// wird das Teil geöffnet (falls nötig), aktiviert, exportiert und – wenn es vorher nicht offen war – wieder geschlossen.
/// </summary>
public static class ModelExport
{
    public static string Extension(ModelFormat f) => f switch
    {
        ModelFormat.Parasolid => ".x_t",
        ModelFormat.JT => ".jt",
        ModelFormat.STEP => ".stp",
        ModelFormat.IGES => ".igs",
        ModelFormat.STL => ".stl",
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    public static string DisplayName(ModelFormat f) => f switch
    {
        ModelFormat.Parasolid => "Parasolid (*.x_t)",
        ModelFormat.JT => "JT (*.jt)",
        ModelFormat.STEP => "STEP (*.stp)",
        ModelFormat.IGES => "IGES (*.igs)",
        ModelFormat.STL => "STL (*.stl)",
        _ => f.ToString(),
    };

    /// <summary>Zieldatei: Name und Ordner wie die PDF der Zeichnung, nur mit anderer Endung.</summary>
    public static string TargetPath(string drawingPdfPath, ModelFormat f) =>
        Path.ChangeExtension(drawingPdfPath, Extension(f));

    /// <summary>Verknüpfung der ersten Hauptansicht (sonst erste Ansicht mit Modell, sonst erste Modellverknüpfung).</summary>
    public static ModelLink? FindModelLink(DraftDocument doc)
    {
        ModelLink? fallback = null;
        foreach (Sheet sheet in doc.Sections.WorkingSection.Sheets)
        {
            foreach (DrawingView view in sheet.DrawingViews)
            {
                ModelLink? link = null;
                try { link = view.ModelLink as ModelLink; } catch { }
                if (link is null) continue;
                if (view.DrawingViewType == DrawingViewTypeConstants.igPrincipleView) return link;
                fallback ??= link;
            }
        }
        if (fallback is not null) return fallback;
        try { return doc.ModelLinks.Count > 0 ? doc.ModelLinks.Item(1) : null; } catch { return null; }
    }

    public static string? FindModelPath(DraftDocument doc)
    {
        try { return FindModelLink(doc)?.FileName; } catch { return null; }
    }

    /// <summary>Exportiert das Modell. Gibt zurück, auf welchem Weg exportiert wurde (für das Protokoll).</summary>
    public static string Export(SeApplication app, DraftDocument draft, string target, bool forceOpen = false)
    {
        var link = FindModelLink(draft) ?? throw new InvalidOperationException(Lang.T("Err.NoModelLink"));
        var modelPath = link.FileName;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target)) File.Delete(target);
        var started = DateTime.Now;

        try
        {
            // 1) Hintergrund: das von der Zeichnung geladene Modell direkt speichern
            if (!forceOpen)
            {
                try
                {
                    if (link.ModelDocument is SeDocument background)
                    {
                        background.SaveCopyAs(target);
                        if (File.Exists(target)) return Lang.T("Log.ModelBackground");
                    }
                }
                catch { /* weiter mit Öffnen */ }
            }

            // 2) Teil öffnen (falls nicht offen), aktivieren, exportieren, Zeichnung wieder aktivieren
            SeDocument? model = null;
            foreach (SeDocument d in app.Documents)
                if (string.Equals(d.FullName, modelPath, StringComparison.OrdinalIgnoreCase)) { model = d; break; }
            bool openedHere = model is null;
            model ??= (SeDocument)app.Documents.Open(modelPath);
            try
            {
                model.Activate();
                model.SaveCopyAs(target);
            }
            finally
            {
                if (openedHere) try { model.Close(false); } catch { }
                try { draft.Activate(); } catch { }
            }
            if (!File.Exists(target))
                throw new IOException(Lang.T("Err.ModelNotCreated", target));
            return Lang.T(openedHere ? "Log.ModelOpened" : "Log.ModelActivated");
        }
        finally
        {
            DeleteTranslatorLog(target, started);
        }
    }

    /// <summary>
    /// Der STEP-Übersetzer legt neben der Zieldatei eine gleichnamige .log-Datei ab.
    /// Sie wird gelöscht, wenn sie bei diesem Export geschrieben wurde (ältere Dateien bleiben unberührt).
    /// </summary>
    private static void DeleteTranslatorLog(string target, DateTime started)
    {
        if (!Path.GetExtension(target).Equals(Extension(ModelFormat.STEP), StringComparison.OrdinalIgnoreCase)) return;
        var log = Path.ChangeExtension(target, ".log");
        try
        {
            if (File.Exists(log) && File.GetLastWriteTime(log) >= started.AddSeconds(-2))
                File.Delete(log);
        }
        catch { /* Protokoll ist nur Beiwerk */ }
    }
}
