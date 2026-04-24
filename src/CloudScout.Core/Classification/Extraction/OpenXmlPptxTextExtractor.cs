using DocumentFormat.OpenXml.Packaging;
using System.Text;

namespace CloudScout.Core.Classification.Extraction;

/// <summary>
/// Extracts text from PowerPoint .pptx presentations via the Microsoft OpenXml SDK. Walks slides
/// in presentation order and accumulates their InnerText. Pptx text is typically stored inline
/// in shape text bodies rather than in a shared table, so there's no lookup step — InnerText on
/// each slide part concatenates every text node under it. Stops once <c>maxChars</c> is hit.
/// </summary>
public sealed class OpenXmlPptxTextExtractor : ITextExtractor
{
    private const string PptxMimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    public bool CanHandle(string? mimeType, string fileName)
    {
        if (string.Equals(mimeType, PptxMimeType, StringComparison.OrdinalIgnoreCase)) return true;
        return fileName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pptm", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> ExtractAsync(Stream content, int maxChars, CancellationToken cancellationToken = default)
    {
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
            using var doc = PresentationDocument.Open(source, isEditable: false);
            var presentationPart = doc.PresentationPart;
            if (presentationPart is null) return Task.FromResult(string.Empty);

            var sb = new StringBuilder();
            foreach (var slidePart in presentationPart.SlideParts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sb.Length >= maxChars) break;

                var slideText = slidePart.Slide.InnerText;
                if (string.IsNullOrEmpty(slideText)) continue;

                sb.Append(slideText);
                sb.Append(' ');
            }

            var extracted = sb.Length > maxChars ? sb.ToString(0, maxChars) : sb.ToString();
            return Task.FromResult(extracted);
        }
        finally
        {
            buffered?.Dispose();
        }
    }
}
