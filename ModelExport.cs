using SolidEdgeDraft;
using SeApplication = SolidEdgeFramework.Application;
using SeDocument = SolidEdgeFramework.SolidEdgeDocument;

namespace SE_RenderDrawing;

/// <summary>Austauschformate für das 3D-Modell.</summary>
public enum ModelFormat { Parasolid, JT, STEP, IGES, STL, Pdf3D }

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
        ModelFormat.Pdf3D => ".pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    public static string DisplayName(ModelFormat f) => f switch
    {
        ModelFormat.Parasolid => "Parasolid (*.x_t)",
        ModelFormat.JT => "JT (*.jt)",
        ModelFormat.STEP => "STEP (*.stp)",
        ModelFormat.IGES => "IGES (*.igs)",
        ModelFormat.STL => "STL (*.stl)",
        ModelFormat.Pdf3D => "3D-PDF (*.pdf)",
        _ => f.ToString(),
    };

    /// <summary>
    /// Zieldatei: Name und Ordner wie die PDF der Zeichnung, nur mit anderer Endung.
    /// Beim 3D-PDF wird <paramref name="pdf3DSuffix"/> angehängt, damit die Zeichnungs-PDF nicht überschrieben wird.
    /// </summary>
    public static string TargetPath(string drawingPdfPath, ModelFormat f, string pdf3DSuffix)
    {
        var dir = Path.GetDirectoryName(drawingPdfPath)!;
        var name = Path.GetFileNameWithoutExtension(drawingPdfPath);
        if (f == ModelFormat.Pdf3D) name += string.IsNullOrEmpty(pdf3DSuffix) ? "_3D" : pdf3DSuffix;
        return Path.Combine(dir, name + Extension(f));
    }

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

        // 3D-PDF: im Hintergrund liefert Solid Edge ein 2D-Bild des aktiven Fensters → immer über das aktivierte Teil
        bool isPdf = Path.GetExtension(target).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        if (isPdf) forceOpen = true;

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
        if (isPdf && !Is3DPdf(target))
        {
            // Solid Edge hat nur eine 2D-PDF erzeugt (Option „3D-PDF“ in Solid Edge nicht aktiv) → nicht als 3D-PDF ablegen
            try { File.Delete(target); } catch { }
            throw new IOException(Lang.T("Err.No3DPdf"));
        }
        return Lang.T(openedHere ? "Log.ModelOpened" : "Log.ModelActivated");
    }

    /// <summary>Prüft, ob die PDF eine 3D-Annotation enthält (auch in komprimierten Objektströmen).</summary>
    public static bool Is3DPdf(string path)
    {
        var data = File.ReadAllBytes(path);
        static bool Has3D(ReadOnlySpan<byte> s) =>
            s.IndexOf("/Subtype/3D"u8) >= 0 || s.IndexOf("/Subtype /3D"u8) >= 0 || s.IndexOf("/3DD"u8) >= 0;
        if (Has3D(data)) return true;

        var marker = "stream"u8.ToArray();
        int pos = 0;
        while ((pos = data.AsSpan(pos).IndexOf(marker) is var i && i >= 0 ? pos + i : -1) >= 0)
        {
            int start = pos + marker.Length;
            if (start < data.Length && data[start] == '\r') start++;
            if (start < data.Length && data[start] == '\n') start++;
            int end = data.AsSpan(start).IndexOf("endstream"u8);
            if (end < 0) break;
            try
            {
                using var z = new System.IO.Compression.ZLibStream(new MemoryStream(data, start, end), System.IO.Compression.CompressionMode.Decompress);
                using var ms = new MemoryStream();
                z.CopyTo(ms);
                if (Has3D(ms.ToArray())) return true;
            }
            catch { /* nicht komprimiert oder anderes Verfahren */ }
            pos = start + end;
        }
        return false;
    }
}
