using CloudScout.Core.Classification.Extraction;
using FluentAssertions;
using System.IO.Compression;
using System.Text;

namespace CloudScout.Core.Tests.Classification.Extraction;

public class OpenDocumentTextExtractorTests
{
    private readonly OpenDocumentTextExtractor _sut = new();

    [Theory]
    [InlineData("letter.odt")]
    [InlineData("BUDGET.ODS")]
    [InlineData("deck.odp")]
    public void CanHandle_recognises_opendocument_by_extension(string fileName)
    {
        _sut.CanHandle(mimeType: null, fileName: fileName).Should().BeTrue();
    }

    [Theory]
    [InlineData("application/vnd.oasis.opendocument.text")]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet")]
    [InlineData("application/vnd.oasis.opendocument.presentation")]
    public void CanHandle_recognises_opendocument_by_mime_type(string mime)
    {
        _sut.CanHandle(mimeType: mime, fileName: "unknown").Should().BeTrue();
    }

    [Fact]
    public void CanHandle_rejects_unrelated_files()
    {
        _sut.CanHandle(mimeType: "application/pdf", fileName: "doc.pdf").Should().BeFalse();
        _sut.CanHandle(mimeType: null, fileName: "report.xlsx").Should().BeFalse();
    }

    [Fact]
    public async Task Extracts_text_from_content_xml()
    {
        using var stream = BuildSyntheticOpenDocument(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                                     xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
              <office:body>
                <office:text>
                  <text:h>Life Insurance Policy</text:h>
                  <text:p>Policy number: LI-20260414</text:p>
                  <text:p>Annual premium due in March.</text:p>
                </office:text>
              </office:body>
            </office:document-content>
            """);

        var text = await _sut.ExtractAsync(stream, maxChars: 5000);

        text.Should().Contain("Life Insurance Policy");
        text.Should().Contain("Policy number: LI-20260414");
        text.Should().Contain("Annual premium");
    }

    [Fact]
    public async Task Returns_empty_when_content_xml_is_missing()
    {
        // An odt/ods/odp without content.xml shouldn't crash the extractor.
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("mimetype");
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes("application/vnd.oasis.opendocument.text"));
        }
        ms.Position = 0;

        var text = await _sut.ExtractAsync(ms, maxChars: 1000);

        text.Should().BeEmpty();
    }

    [Fact]
    public async Task Respects_maxChars_cap()
    {
        var bigText = new string('X', 2000);
        using var stream = BuildSyntheticOpenDocument(
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                                     xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
              <office:body><office:text><text:p>{bigText}</text:p></office:text></office:body>
            </office:document-content>
            """);

        var text = await _sut.ExtractAsync(stream, maxChars: 100);

        text.Length.Should().BeLessThanOrEqualTo(100);
    }

    /// <summary>
    /// Builds a minimal OpenDocument-shaped ZIP containing just <c>content.xml</c>.
    /// The extractor only reads that entry, so the other structural pieces (mimetype,
    /// manifest) aren't required for the extraction test.
    /// </summary>
    private static MemoryStream BuildSyntheticOpenDocument(string contentXml)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("content.xml");
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(contentXml);
            stream.Write(bytes);
        }
        ms.Position = 0;
        return ms;
    }
}
