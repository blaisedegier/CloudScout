using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Text;

namespace CloudScout.Core.Classification.Extraction;

/// <summary>
/// Extracts text from Excel .xlsx spreadsheets via the Microsoft OpenXml SDK. Iterates every
/// sheet in reading order; for each cell, resolves shared-string references against the
/// workbook's <see cref="SharedStringTablePart"/> (most text in xlsx lives there, not inline).
/// Stops as soon as <c>maxChars</c> is reached so large sheets don't dominate memory.
/// </summary>
public sealed class OpenXmlXlsxTextExtractor : ITextExtractor
{
    private const string XlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public bool CanHandle(string? mimeType, string fileName)
    {
        if (string.Equals(mimeType, XlsxMimeType, StringComparison.OrdinalIgnoreCase)) return true;
        return fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> ExtractAsync(Stream content, int maxChars, CancellationToken cancellationToken = default)
    {
        // OpenXml requires a seekable stream. Mirror the PDF/DOCX extractor pattern.
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
            using var doc = SpreadsheetDocument.Open(source, isEditable: false);
            var workbookPart = doc.WorkbookPart;
            if (workbookPart is null) return Task.FromResult(string.Empty);

            // Build the shared-string lookup once — most cells point into it by index.
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
                ?.Elements<SharedStringItem>()
                .Select(s => s.InnerText)
                .ToList();

            var sb = new StringBuilder();

            foreach (var worksheetPart in workbookPart.WorksheetParts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sb.Length >= maxChars) break;

                foreach (var cell in worksheetPart.Worksheet.Descendants<Cell>())
                {
                    if (sb.Length >= maxChars) break;

                    var text = ResolveCellText(cell, sharedStrings);
                    if (string.IsNullOrEmpty(text)) continue;

                    sb.Append(text);
                    sb.Append(' ');
                }
            }

            var extracted = sb.Length > maxChars ? sb.ToString(0, maxChars) : sb.ToString();
            return Task.FromResult(extracted);
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static string ResolveCellText(Cell cell, List<string>? sharedStrings)
    {
        var value = cell.CellValue?.InnerText ?? cell.InnerText;
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Shared-string cells store an integer index into the workbook's string table.
        // Inline strings and numbers are used as-is.
        if (cell.DataType?.Value == CellValues.SharedString
            && sharedStrings is not null
            && int.TryParse(value, out var idx)
            && idx >= 0 && idx < sharedStrings.Count)
        {
            return sharedStrings[idx];
        }

        return value;
    }
}
