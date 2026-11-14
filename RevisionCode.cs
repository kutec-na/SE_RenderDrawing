namespace SE_RenderDrawing;

/// <summary>Zählt Revisionsstände hoch: AA → AB … AZ → BA, ZZ → AAA, 01 → 02, A1 → A2.</summary>
public static class RevisionCode
{
    /// <summary>Nächste Revision oder null, wenn das Format nicht erkannt wird.</summary>
    public static string? Next(string? current)
    {
        if (string.IsNullOrWhiteSpace(current))
            return null;

        var s = current.Trim();

        // Endet auf Ziffern → Zahlenteil hochzählen (Breite beibehalten)
        int digitStart = s.Length;
        while (digitStart > 0 && char.IsAsciiDigit(s[digitStart - 1])) digitStart--;
        if (digitStart < s.Length)
        {
            var digits = s[digitStart..];
            if (!long.TryParse(digits, out var number)) return null;
            return s[..digitStart] + (number + 1).ToString().PadLeft(digits.Length, '0');
        }

        // Endet auf Buchstaben → Buchstabenteil zur Basis 26 hochzählen
        int letterStart = s.Length;
        while (letterStart > 0 && char.IsAsciiLetter(s[letterStart - 1])) letterStart--;
        if (letterStart == s.Length)
            return null;

        var chars = s[letterStart..].ToCharArray();
        int i = chars.Length - 1;
        while (i >= 0)
        {
            char z = char.IsUpper(chars[i]) ? 'Z' : 'z';
            if (chars[i] != z) { chars[i]++; break; }
            chars[i] = char.IsUpper(chars[i]) ? 'A' : 'a';
            i--;
        }
        var letters = new string(chars);
        if (i < 0) letters = (char.IsUpper(chars[0]) ? 'A' : 'a') + letters;
        return s[..letterStart] + letters;
    }
}
