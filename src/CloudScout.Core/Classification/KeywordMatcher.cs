namespace CloudScout.Core.Classification;

/// <summary>
/// Shared word-boundary matcher used by Tier 0 (filename/folder) and Tier 1 (content). Substring
/// matching produces too many false positives for short keywords ("id" inside "Suicidepiano",
/// "cor" inside "Discord", "lab" inside "Streamlabs"), so all keyword checks must respect word
/// boundaries. A boundary is any non-letter/digit character or the start/end of the string.
/// </summary>
internal static class KeywordMatcher
{
    /// <summary>
    /// True when <paramref name="word"/> appears as a whole-token run in <paramref name="text"/>.
    /// Case-insensitive. Multi-word keywords (e.g. "id card", "log book") match across any
    /// non-alphanumeric separator in the haystack — so "id_card.png", "ID-Card.pdf", and
    /// "id card.pdf" all match the keyword "id card".
    /// </summary>
    public static bool ContainsWord(string text, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word)) return false;

        // For multi-word keywords, normalise both sides: collapse every non-alphanumeric run to a
        // single space so separator differences (underscore, hyphen, dot) don't block matches.
        if (HasInternalSeparator(word))
        {
            var nText = NormaliseSeparators(text);
            var nWord = NormaliseSeparators(word);
            return WholeWordMatch(nText, nWord);
        }

        return WholeWordMatch(text, word);
    }

    private static bool WholeWordMatch(string text, string word)
    {
        var idx = 0;
        while ((idx = text.IndexOf(word, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var leftOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            var rightBoundary = idx + word.Length;
            var rightOk = rightBoundary == text.Length || !char.IsLetterOrDigit(text[rightBoundary]);
            if (leftOk && rightOk) return true;
            idx += word.Length;
        }
        return false;
    }

    private static bool HasInternalSeparator(string word)
    {
        foreach (var ch in word)
            if (!char.IsLetterOrDigit(ch)) return true;
        return false;
    }

    private static string NormaliseSeparators(string s)
    {
        var buf = new System.Text.StringBuilder(s.Length);
        var lastWasSpace = false;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buf.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                buf.Append(' ');
                lastWasSpace = true;
            }
        }
        return buf.ToString();
    }
}
