using System.Text;

namespace CloudScout.Core.Classification.Extraction;

/// <summary>
/// Extracts plain text from Rich Text Format (.rtf) documents.
///
/// RTF is a structured text format: <c>{</c> and <c>}</c> delimit groups, <c>\word</c> are
/// control words, <c>\X</c> (single non-letter) are control symbols. Visible document text is
/// whatever's left after stripping those. Certain groups starting with a "destination" control
/// word (<c>{\fonttbl ...}</c>, <c>{\colortbl ...}</c>, <c>{\stylesheet ...}</c>, <c>{\info ...}</c>,
/// <c>{\pict ...}</c>) carry metadata the user doesn't see — the parser skips them entirely.
///
/// Good enough for classification: we need words, not perfect fidelity. No dependency, no native
/// binaries. Specifically avoids WinForms' <c>RichTextBox</c> which would pull a desktop UI stack
/// into the Core library.
/// </summary>
public sealed class RtfTextExtractor : ITextExtractor
{
    // RTF destination control words whose group contents are metadata, not visible text.
    // Not exhaustive (RTF has ~30+ destinations) but covers the ones that leak noise into
    // classification for typical documents.
    private static readonly HashSet<string> SkipDestinations = new(StringComparer.OrdinalIgnoreCase)
    {
        "fonttbl", "colortbl", "stylesheet", "info",
        "pict", "header", "footer", "footnote", "comment",
        "author", "operator", "creatim", "revtim", "printim",
        "title", "subject", "keywords", "doccomm", "version",
        "generator", "latentstyles", "themedata",
    };

    public bool CanHandle(string? mimeType, string fileName)
    {
        if (string.Equals(mimeType, "application/rtf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mimeType, "text/rtf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return fileName.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> ExtractAsync(Stream content, int maxChars, CancellationToken cancellationToken = default)
    {
        // RTF files are ASCII-compatible; read as UTF-8 with fallback. Larger files can be
        // truncated at read time since the whole file is the input to the parser.
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        return Parse(raw, maxChars);
    }

    internal static string Parse(string input, int maxChars)
    {
        var sb = new StringBuilder();
        var depth = 0;          // current group nesting depth
        var skipDepth = -1;     // depth at which a skip-destination group started; -1 = not skipping
        var i = 0;

        while (i < input.Length && sb.Length < maxChars)
        {
            var c = input[i];

            if (c == '\\')
            {
                i++;
                if (i >= input.Length) break;

                var next = input[i];
                if (char.IsLetter(next))
                {
                    // Control word: letters, optional signed numeric parameter, optional space delimiter
                    var wordStart = i;
                    while (i < input.Length && char.IsLetter(input[i])) i++;
                    var word = input.Substring(wordStart, i - wordStart);

                    if (i < input.Length && (input[i] == '-' || char.IsDigit(input[i])))
                    {
                        if (input[i] == '-') i++;
                        while (i < input.Length && char.IsDigit(input[i])) i++;
                    }
                    if (i < input.Length && input[i] == ' ') i++;

                    // Paragraph/line/tab breaks translate to whitespace so words stay separated
                    if (skipDepth < 0 && (word == "par" || word == "line" || word == "tab"))
                        sb.Append(' ');

                    // Entering a destination we want to skip until its group closes
                    if (skipDepth < 0 && SkipDestinations.Contains(word))
                        skipDepth = depth;
                }
                else
                {
                    // Control symbol: escaped brace / backslash / other single char
                    i++;
                    if (skipDepth < 0)
                    {
                        switch (next)
                        {
                            case '\\': sb.Append('\\'); break;
                            case '{': sb.Append('{'); break;
                            case '}': sb.Append('}'); break;
                            // Other symbols (\~ nbsp, \- optional hyphen, etc.) are dropped
                        }
                    }
                }
            }
            else if (c == '{')
            {
                depth++;
                i++;
            }
            else if (c == '}')
            {
                depth--;
                // Closing the group that started the skip: resume collecting text. Use `<`
                // rather than `<=` because skipDepth holds the depth *inside* the destination
                // group; its closing brace drops depth one level below that.
                if (skipDepth >= 0 && depth < skipDepth)
                    skipDepth = -1;
                i++;
            }
            else
            {
                // Raw newlines inside the RTF source carry no document meaning; ignore them.
                if (skipDepth < 0 && c != '\r' && c != '\n')
                    sb.Append(c);
                i++;
            }
        }

        return sb.Length > maxChars ? sb.ToString(0, maxChars) : sb.ToString();
    }
}
