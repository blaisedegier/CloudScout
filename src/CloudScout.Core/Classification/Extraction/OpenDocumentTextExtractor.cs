using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace CloudScout.Core.Classification.Extraction;

/// <summary>
/// Extracts text from OpenDocument Format files (.odt text documents, .ods spreadsheets,
/// .odp presentations). All three share the same container structure: a ZIP archive whose
/// <c>content.xml</c> entry holds the document body in the OpenDocument XML schema.
///
/// Rather than interpret the schema (which differs between text/spreadsheet/presentation),
/// this extractor flattens <c>content.xml</c> into its text nodes and concatenates them.
/// Tests show this is sufficient for keyword-matching classification — we don't need
/// styling or structure, just the words on the page.
///
/// Uses only BCL APIs (System.IO.Compression + System.Xml.Linq) — no NuGet dependency.
/// </summary>
public sealed class OpenDocumentTextExtractor : ITextExtractor
{
    // The ZIP entry that holds the main document body in every OpenDocument format.
    private const string ContentEntryName = "content.xml";

    private static readonly string[] SupportedExtensions = [".odt", ".ods", ".odp"];
    private static readonly string[] SupportedMimes =
    [
        "application/vnd.oasis.opendocument.text",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/vnd.oasis.opendocument.presentation",
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

    public Task<string> ExtractAsync(Stream content, int maxChars, CancellationToken cancellationToken = default)
    {
        // ZipArchive needs a seekable stream. Buffer if the caller handed us a network stream.
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
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            var contentEntry = archive.GetEntry(ContentEntryName);
            if (contentEntry is null) return Task.FromResult(string.Empty);

            using var entryStream = contentEntry.Open();
            var xml = XDocument.Load(entryStream, LoadOptions.PreserveWhitespace);

            var sb = new StringBuilder();
            AppendTextNodes(xml.Root, sb, maxChars, cancellationToken);

            var extracted = sb.Length > maxChars ? sb.ToString(0, maxChars) : sb.ToString();
            return Task.FromResult(extracted);
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    /// <summary>
    /// Walks every descendant element and appends its direct text children (XText nodes) to the
    /// builder. Yields a flat stream of the words in the document without caring about element
    /// names or namespaces — works uniformly for text, spreadsheet, and presentation variants.
    /// </summary>
    private static void AppendTextNodes(XElement? element, StringBuilder sb, int maxChars, CancellationToken ct)
    {
        if (element is null) return;

        foreach (var node in element.DescendantNodesAndSelf().OfType<XText>())
        {
            if (sb.Length >= maxChars) return;
            ct.ThrowIfCancellationRequested();

            var value = node.Value;
            if (string.IsNullOrWhiteSpace(value)) continue;

            sb.Append(value);
            sb.Append(' ');
        }
    }
}
