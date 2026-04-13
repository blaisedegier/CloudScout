using DocumentFormat.OpenXml.Packaging;

namespace CloudScout.Core.Classification.Extraction;

/// <summary>
/// Extracts text from Word .docx files using the first-party Microsoft OpenXml SDK. Reads the
/// flattened <c>InnerText</c> of the document body — sufficient for keyword classification and
/// much faster than walking each paragraph/run. Older .doc binary format is *not* supported;
/// it requires a separate library (NPOI, Aspose). V1 leaves those files unclassified.
/// </summary>
public sealed class OpenXmlDocxTextExtractor : ITextExtractor
{
    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public bool CanHandle(string? mimeType, string fileName)
    {
        if (string.Equals(mimeType, DocxMimeType, StringComparison.OrdinalIgnoreCase)) return true;
        return fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> ExtractAsync(Stream content, int maxChars, CancellationToken cancellationToken = default)
    {
        // OpenXml requires a seekable stream; mirror the PdfPig buffering pattern.
        Stream source = content;
        MemoryStream? buffered = null;
        if (!content.CanSeek)
        {
            buffered = new MemoryStream();
            content.CopyTo(buffered);
            buffered.Position = 0;
            source = buffered;
        }

        try
        {
            using var doc = WordprocessingDocument.Open(source, isEditable: false);
            var body = doc.MainDocumentPart?.Document?.Body;
            var text = body?.InnerText ?? string.Empty;
            if (text.Length > maxChars) text = text.Substring(0, maxChars);
            return Task.FromResult(text);
        }
        finally
        {
            buffered?.Dispose();
        }
    }
}
