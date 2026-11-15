using SolidEdgeDraft;
using SeApplication = SolidEdgeFramework.Application;
using GlobalParam = SolidEdgeFramework.ApplicationGlobalConstants;

namespace SE_RenderDrawing;

public static class DrawingActions
{
    /// <summary>Aktualisiert alle veralteten Zeichnungsansichten. Gibt die Anzahl aktualisierter Ansichten zurück.</summary>
    public static int UpdateViews(DraftDocument doc)
    {
        int updated = 0;
        foreach (Sheet sheet in doc.Sections.WorkingSection.Sheets)
        {
            foreach (DrawingView view in sheet.DrawingViews)
            {
                if (view.IsUpToDate) continue;
                view.Update();
                updated++;
            }
        }
        return updated;
    }

    /// <summary>Anzahl abgehängter/fehlerhafter Bemaßungen auf den Arbeitsblättern.</summary>
    public static int CountDetachedDimensions(DraftDocument doc)
    {
        int count = 0;
        foreach (Sheet sheet in doc.Sections.WorkingSection.Sheets)
        {
            foreach (SolidEdgeFrameworkSupport.Dimension dim in (SolidEdgeFrameworkSupport.Dimensions)sheet.Dimensions)
            {
                var status = (int)dim.StatusOfDimension;
                // 1 = abgehängt, 2 = Fehler, 5 = ein Ende abgehängt
                if (status is 1 or 2 or 5) count++;
            }
        }
        return count;
    }

    /// <summary>Löscht die Hinweise der Bemaßungsverfolgung. Gibt die Anzahl gelöschter Hinweise zurück.</summary>
    public static int ClearDimensionTracker(DraftDocument doc)
    {
        int n = 0;
        doc.DimensionTrackerNumEntries(out n);
        if (n > 0) doc.ClearAllDimensionTrackerEntries();
        return n;
    }

    /// <summary>Aktuelle Solid-Edge-Einstellung „Alle Farben schwarz speichern“ beim PDF-Export (null = nicht lesbar).</summary>
    public static bool? GetSaveAllColorsBlack(SeApplication app)
    {
        try
        {
            object value = false;
            app.GetGlobalParameter(GlobalParam.seApplicationGlobalDraftSaveAsPDFSaveAllColorsBlack, ref value);
            return Convert.ToBoolean(value);
        }
        catch { return null; }
    }

    /// <summary>
    /// Speichert die Zeichnung als PDF. <paramref name="allColorsBlack"/> setzt die Solid-Edge-Option
    /// „Alle Farben schwarz speichern“ nur für diesen Export; danach wird der vorherige Wert wiederhergestellt.
    /// </summary>
    public static void ExportPdf(SeApplication app, DraftDocument doc, string pdfPath, bool allColorsBlack)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
        if (File.Exists(pdfPath)) File.Delete(pdfPath);

        var previous = GetSaveAllColorsBlack(app);
        if (previous != allColorsBlack)
            app.SetGlobalParameter(GlobalParam.seApplicationGlobalDraftSaveAsPDFSaveAllColorsBlack, allColorsBlack);
        try
        {
            doc.SaveCopyAs(pdfPath);
        }
        finally
        {
            if (previous is { } old && old != allColorsBlack)
                try { app.SetGlobalParameter(GlobalParam.seApplicationGlobalDraftSaveAsPDFSaveAllColorsBlack, old); } catch { }
        }
        if (!File.Exists(pdfPath))
            throw new IOException(Lang.T("Err.PdfNotCreated", pdfPath));
    }
}
