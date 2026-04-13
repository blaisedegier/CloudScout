namespace CloudScout.Core.Classification.Extraction;

/// <summary>
/// Extracts text from files whose content *is* text (TXT, CSV, MD, LOG). Respects the
/// <c>maxChars</c> cap so a 100MB CSV doesn't get fully loaded for classification. Assumes UTF-8
/// with a fallback to the system default encoding — good enough for the general case.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    private static readonly string[] SupportedExtensions = [".txt", ".csv", ".md", ".log"];
    private static readonly string[] SupportedMimes =
    [
        "text/plain", "text/csv", "text/markdown", "text/x-log",
    ];

    public bool CanHandle(string? mimeType, string fileName)
    {
        if (!string.IsNullOrEmpty(mimeType) &&
            SupportedMimes.Any(m => string.Equals(m, mimeType, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return SupportedExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> ExtractAsync(Stream content, int maxChars, CancellationToken cancellationToken = default)
    {
        // StreamReader with detectEncodingFromByteOrderMarks=true handles UTF-8 BOMs and UTF-16
        // correctly without us needing to sniff manually.
        using var reader = new StreamReader(content, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var buffer = new char[maxChars];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return new string(buffer, 0, read);
    }
}
