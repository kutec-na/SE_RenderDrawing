using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace SE_RenderDrawing;

/// <summary>
/// Übersetzungen aus <c>SE_RenderDrawing.lang.json</c> (neben der EXE).
/// Oberste Ebene: je Sprache eine Gruppe ("DE", "EN", …), dazu "_maxLength" (Höchstlänge einzelner Beschriftungen).
/// Rückfall je Text: gewählte Sprache → EN aus der Datei → EN aus der eingebetteten Standarddatei → Schlüssel.
/// Fehlt die Datei, wird sie aus der eingebetteten Standarddatei angelegt.
/// </summary>
public static class Lang
{
    public const string FileName = "SE_RenderDrawing.lang.json";
    public const string Fallback = "EN";
    private const string MaxLengthGroup = "_maxLength";

    private static Dictionary<string, string> _selected = new();
    private static Dictionary<string, string> _english = new();
    private static Dictionary<string, string> _builtInEnglish = new();
    private static Dictionary<string, int> _maxLength = new();

    /// <summary>Tatsächlich verwendete Sprache (nach Rückfall).</summary>
    public static string Current { get; private set; } = Fallback;
    /// <summary>In der Datei vorhandene Sprachen.</summary>
    public static IReadOnlyList<string> Available { get; private set; } = [];
    /// <summary>Problem beim Laden der Sprachdatei (null = alles in Ordnung).</summary>
    public static string? LoadError { get; private set; }

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static void Load(string? language)
    {
        LoadError = null;
        var builtInJson = ReadEmbedded();
        var builtIn = Parse(builtInJson);
        _builtInEnglish = builtIn.Groups.GetValueOrDefault(Fallback) ?? new();

        Parsed file = builtIn;
        try
        {
            if (File.Exists(FilePath)) file = Parse(File.ReadAllText(FilePath));
            else File.WriteAllText(FilePath, builtInJson);
        }
        catch (Exception ex)
        {
            LoadError = $"{FileName}: {ex.Message}";
            file = builtIn;
        }

        Available = file.Groups.Keys.ToList();
        _english = file.Groups.GetValueOrDefault(Fallback) ?? _builtInEnglish;
        // Höchstlängen: Datei vor Standard
        _maxLength = new Dictionary<string, int>(builtIn.MaxLength, StringComparer.Ordinal);
        foreach (var (k, v) in file.MaxLength) _maxLength[k] = v;

        var code = (language ?? string.Empty).Trim();
        var match = file.Groups.Keys.FirstOrDefault(k => k.Equals(code, StringComparison.OrdinalIgnoreCase));
        Current = match ?? Fallback;
        _selected = match is null ? _english : file.Groups[match];
    }

    /// <summary>Übersetzter Text (mit optionalen Platzhaltern {0}, {1} …).</summary>
    public static string T(string key, params object?[] args)
    {
        var text = Lookup(key);
        if (args.Length == 0) return text;
        try { return string.Format(CultureInfo.CurrentCulture, text, args); }
        catch (FormatException) { return text + " " + string.Join(" ", args); } // fehlerhafte Platzhalter in der Übersetzung
    }

    /// <summary>Beschriftung: wie <see cref="T"/>, aber auf die Höchstlänge aus "_maxLength" gekürzt (am Ende abgeschnitten).</summary>
    public static string L(string key)
    {
        var text = Lookup(key);
        return _maxLength.TryGetValue(key, out var max) && max > 0 && text.Length > max ? text[..max] : text;
    }

    private static string Lookup(string key) =>
        _selected.TryGetValue(key, out var s) ? s
        : _english.TryGetValue(key, out s) ? s
        : _builtInEnglish.TryGetValue(key, out s) ? s
        : key;

    private sealed record Parsed(Dictionary<string, Dictionary<string, string>> Groups, Dictionary<string, int> MaxLength);

    private static Parsed Parse(string json)
    {
        var groups = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var max = new Dictionary<string, int>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var group in doc.RootElement.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Object) continue; // z. B. "_info"
            if (group.Name == MaxLengthGroup)
            {
                foreach (var e in group.Value.EnumerateObject())
                    if (e.Value.TryGetInt32(out var n)) max[e.Name] = n;
                continue;
            }
            if (group.Name.StartsWith('_')) continue;
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in group.Value.EnumerateObject())
                if (e.Value.ValueKind == JsonValueKind.String) dict[e.Name] = e.Value.GetString()!;
            groups[group.Name] = dict;
        }
        return new Parsed(groups, max);
    }

    private static string ReadEmbedded()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(FileName)
            ?? throw new InvalidOperationException($"Eingebettete Ressource {FileName} fehlt.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
