using System.Text;
using UglyToad.PdfPig;

namespace CloudScout.Core.Classification.Extraction;

/// <summary>
/// Extracts text from PDFs using PdfPig. Stops reading once <c>maxChars</c> is reached so
/// multi-hundred-page PDFs don't dominate memory. Does not perform OCR — scanned PDFs without
/// an embedded text layer will return empty text, at which point Tier 3 (multimodal LLM) takes over.
/// </summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    public bool CanHandle(string? mimeType, string fileName)
    {
        if (string.Equals(mimeType, "application/pdf", StringComparison.OrdinalIgnoreCase)) return true;
        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> ExtractAsync(Stream content, int maxChars, CancellationToken cancellationToken = default)
    {
        // PdfPig wants a seekable stream. Buffer into MemoryStream if we can't seek.
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
            var sb = new StringBuilder();
            using var document = PdfDocument.Open(source);
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.Append(page.Text);
                sb.Append(' ');
                if (sb.Length >= maxChars) break;
            }

            var text = sb.Length > maxChars ? sb.ToString(0, maxChars) : sb.ToString();
            return Task.FromResult(text);
        }
        finally
        {
            buffered?.Dispose();
        }
    }
}
