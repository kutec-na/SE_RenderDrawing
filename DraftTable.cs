using System.Globalization;
using SolidEdgeDraft;

namespace SE_RenderDrawing;

/// <summary>
/// Eine Tabelle der Zeichnung (Solid Edge "Table"), deren Inhalt beim Öffnen einmal gelesen wird.
/// Zeile 1 ist die älteste; neue Einträge kommen in die erste freie Zeile nach dem letzten Eintrag.
/// Wie herum die Tabelle angezeigt wird (von unten nach oben), bestimmt Solid Edge (<see cref="ReverseDisplay"/>).
/// </summary>
public sealed class DraftTable
{
    private static readonly string[] DateFormats =
        ["dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy/MM/dd"];

    private readonly Table _table;

    /// <summary>Position in der Reihenfolge, in der Solid Edge die Tabellen liefert (1-basiert).</summary>
    public int Number { get; }
    public string Name { get; }
    public string[] Headers { get; }
    public bool ReverseDisplay { get; }
    /// <summary>Zellinhalte [Zeile][Spalte], 0-basiert.</summary>
    public List<string[]> Rows { get; } = new();

    public int ColumnCount => Headers.Length;
    public int Capacity => Rows.Count;
    public int FilledCount => Rows.Count(r => !IsEmpty(r));
    public int LastFilledRow => Rows.FindLastIndex(r => !IsEmpty(r)); // 0-basiert, -1 = leer
    public bool IsFull => Rows.Count > 0 && !IsEmpty(Rows[^1]);

    private DraftTable(Table table, int number)
    {
        _table = table;
        Number = number;
        try { Name = table.Name; } catch { Name = $"Tabelle {number}"; }
        var cols = table.Columns;
        Headers = new string[cols.Count];
        for (int c = 0; c < Headers.Length; c++)
            Headers[c] = (cols.Item(c + 1).Header ?? string.Empty).Trim();
        try { ReverseDisplay = table.ReverseDisplayOrder; } catch { }
        Reload();
    }

    public static List<DraftTable> ReadAll(DraftDocument doc)
    {
        var list = new List<DraftTable>();
        int n = 0;
        foreach (Table t in doc.Tables)
        {
            n++;
            try { list.Add(new DraftTable(t, n)); }
            catch { /* Tabelle ohne lesbare Zellen überspringen */ }
        }
        return list;
    }

    private void Reload()
    {
        Rows.Clear();
        int rows = _table.Rows.Count;
        for (int r = 1; r <= rows; r++)
        {
            var cells = new string[ColumnCount];
            for (int c = 1; c <= ColumnCount; c++)
                cells[c - 1] = (_table.Cell[r, c].value ?? string.Empty).Trim();
            Rows.Add(cells);
        }
    }

    private static bool IsEmpty(string[] row) => row.All(string.IsNullOrWhiteSpace);

    /// <summary>Letzter Eintrag in einer Spalte (0-basiert) oder null.</summary>
    public string? LastValue(int column)
    {
        if (column < 0 || column >= ColumnCount) return null;
        for (int r = Rows.Count - 1; r >= 0; r--)
            if (!string.IsNullOrWhiteSpace(Rows[r][column])) return Rows[r][column];
        return null;
    }

    public bool ContainsValue(int column, string value) =>
        column >= 0 && column < ColumnCount &&
        Rows.Any(r => string.Equals(r[column], value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Jüngstes Datum, das in irgendeiner Zelle steht.</summary>
    public DateTime? NewestDate()
    {
        DateTime? best = null;
        foreach (var row in Rows)
            foreach (var cell in row)
                if (TryParseDate(cell, out var d) && (best is null || d > best)) best = d;
        return best;
    }

    public static bool TryParseDate(string s, out DateTime date) =>
        DateTime.TryParseExact(s.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>
    /// Trägt Werte (Spalte 0-basiert → Text) in die Zeile nach dem letzten Eintrag ein.
    /// Ist die Tabelle voll, wird die älteste Zeile verworfen und alle übrigen rücken eine Zeile nach.
    /// </summary>
    /// <returns>Die verworfene Zeile oder null.</returns>
    public string[]? Add(IReadOnlyDictionary<int, string> values)
    {
        if (Capacity == 0) throw new InvalidOperationException("Die Tabelle hat keine Zeilen.");
        string[]? dropped = null;
        int target = LastFilledRow + 1; // 0-basiert
        if (target >= Capacity)
        {
            dropped = Rows[0];
            for (int r = 0; r < Capacity - 1; r++)
                WriteRow(r, Rows[r + 1]);
            target = Capacity - 1;
        }
        var row = new string[ColumnCount];
        foreach (var (col, text) in values)
            if (col >= 0 && col < ColumnCount) row[col] = text;
        WriteRow(target, row);
        try { _table.Update(); } catch { /* nicht bei jeder Tabellenart nötig */ }
        Reload();
        return dropped;
    }

    private void WriteRow(int row, string?[] cells)
    {
        for (int c = 0; c < ColumnCount; c++)
            _table.Cell[row + 1, c + 1].value = cells[c] ?? string.Empty;
    }

    // ---------- Spaltenzuordnung und Auswahl ----------

    /// <summary>Ordnet die Felder den Spalten anhand der Spaltenköpfe zu (−1 = nicht gefunden).</summary>
    public Dictionary<Field, int> GuessColumns(MacroConfig cfg)
    {
        var map = new Dictionary<Field, int>();
        var used = new HashSet<int>();
        foreach (var f in Enum.GetValues<Field>())
        {
            int col = FindColumn(cfg.ColumnHeaders[f], used, exact: true);
            if (col < 0) col = FindColumn(cfg.ColumnHeaders[f], used, exact: false);
            map[f] = col;
            if (col >= 0) used.Add(col);
        }
        return map;
    }

    private int FindColumn(string[] names, HashSet<int> used, bool exact)
    {
        foreach (var name in names)
            for (int c = 0; c < ColumnCount; c++)
            {
                if (used.Contains(c) || Headers[c].Length == 0) continue;
                bool hit = exact
                    ? Headers[c].Equals(name, StringComparison.OrdinalIgnoreCase)
                    : Headers[c].StartsWith(name, StringComparison.OrdinalIgnoreCase);
                if (hit) return c;
            }
        return -1;
    }

    /// <summary>Sieht aus wie eine Änderungshistorie: Spalten für Revision und Beschreibung erkennbar.</summary>
    public bool IsRevisionHistory(MacroConfig cfg)
    {
        var map = GuessColumns(cfg);
        return map[Field.Revision] >= 0 && map[Field.Description] >= 0;
    }

    /// <summary>
    /// Automatische Auswahl der Änderungshistorie:
    /// 1. Tabellen, die wie eine Änderungshistorie aussehen (sonst alle),
    /// 2. davon bevorzugt mit 4 Spalten,
    /// 3. davon die mit dem jüngsten Datum in einer der Zellen,
    /// 4. bei Gleichstand die mit der höchsten Nummer.
    /// </summary>
    public static DraftTable? AutoSelect(IReadOnlyList<DraftTable> tables, MacroConfig cfg)
    {
        IEnumerable<DraftTable> set = tables.Where(t => t.Capacity > 0).ToList();
        if (!set.Any()) return null;

        var history = set.Where(t => t.IsRevisionHistory(cfg)).ToList();
        if (history.Count > 0) set = history;

        var four = set.Where(t => t.ColumnCount == 4).ToList();
        if (four.Count > 0) set = four;

        return set
            .OrderByDescending(t => t.NewestDate() ?? DateTime.MinValue)
            .ThenByDescending(t => t.Number)
            .First();
    }
}
