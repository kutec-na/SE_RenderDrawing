namespace SE_RenderDrawing;

/// <summary>
/// PDF-Dateiname aus dem Namen der Zeichnung, optional mit eingefügter Revision (siehe <see cref="FileNameRule"/>),
/// in einem Ordner, der absolut oder relativ zum Ordner der .dft angegeben wird (z. B. ".\Zeichnungen\").
/// </summary>
public static class PdfNaming
{
    /// <summary>
    /// Fügt die Revision beim n-ten Trennzeichen ein (fehlt es, am Ende):
    /// vorstehend → Name[..pos] + Präfix + Rev + Name[pos..]   ("ABC_12-3456-789" + "-AC" + "_Bauteil")
    /// nachstehend → Name[..pos+Trenner] + Rev + Suffix + Rest ("ABC_12-3456-789_" + "AC-" + "Bauteil")
    /// </summary>
    public static string FileName(string draftPath, string revision, FileNameRule rule)
    {
        var name = Path.GetFileNameWithoutExtension(draftPath);
        if (!rule.AppendRevision || string.IsNullOrEmpty(revision))
            return name + ".pdf";

        var sep = rule.Separator ?? string.Empty;
        int pos = -1; // Index des n-ten Trennzeichens
        if (sep.Length > 0 && rule.Count > 0)
        {
            int from = 0;
            for (int n = 0; n < rule.Count; n++)
            {
                pos = name.IndexOf(sep, from, StringComparison.Ordinal);
                if (pos < 0) break;
                from = pos + sep.Length;
            }
        }
        else if (rule.Count <= 0)
            pos = 0; // Anfang des Namens

        string result;
        if (rule.Position == RevisionPosition.Before)
        {
            int at = pos < 0 ? name.Length : pos;
            result = name[..at] + rule.Affix + revision + name[at..];
        }
        else
        {
            result = pos < 0
                ? name + sep + revision                                   // kein n-tes Trennzeichen: hinten anhängen
                : rule.Count <= 0
                    ? revision + rule.Affix + name                        // Anzahl 0: ganz vorne
                    : name[..(pos + sep.Length)] + revision + rule.Affix + name[(pos + sep.Length)..];
        }
        return result + ".pdf";
    }

    /// <summary>
    /// Löst den Speicherort auf: absolut bleibt absolut, relativ bezieht sich auf den Ordner der Zeichnung,
    /// leer = Ordner der Zeichnung. Umgebungsvariablen (%USERPROFILE% …) werden ersetzt.
    /// </summary>
    public static string ResolveFolder(string draftPath, string? folder)
    {
        var dir = Path.GetDirectoryName(draftPath) ?? throw new InvalidOperationException("Zeichnung hat keinen Pfad (noch nicht gespeichert?)");
        var f = Environment.ExpandEnvironmentVariables((folder ?? string.Empty).Trim().Trim('"'));
        if (f.Length == 0) return dir;
        return Path.GetFullPath(Path.IsPathFullyQualified(f) ? f : Path.Combine(dir, f));
    }

    public static string FullPath(string draftPath, string revision, string? folder, FileNameRule rule) =>
        Path.Combine(ResolveFolder(draftPath, folder), FileName(draftPath, revision, rule));

    /// <summary>Für die Anzeige: Ordner unterhalb der Zeichnung relativ (".\Unterordner\"), sonst absolut.</summary>
    public static string ToDisplayFolder(string draftPath, string folder)
    {
        var dir = Path.GetDirectoryName(draftPath)!;
        var rel = Path.GetRelativePath(dir, folder);
        if (rel == ".") return @".\";
        if (rel.StartsWith("..") || Path.IsPathRooted(rel)) return folder.TrimEnd('\\') + @"\";
        return @".\" + rel.TrimEnd('\\') + @"\";
    }
}
