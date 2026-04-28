using CloudScout.Core.Classification.Extraction;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;

namespace CloudScout.Core.Tests.Classification.Extraction;

public class OpenXmlXlsxTextExtractorTests
{
    private readonly OpenXmlXlsxTextExtractor _sut = new();

    [Theory]
    [InlineData("report.xlsx")]
    [InlineData("BUDGET.XLSX")]
    [InlineData("macros.xlsm")]
    public void CanHandle_recognises_xlsx_by_extension(string fileName)
    {
        _sut.CanHandle(mimeType: null, fileName: fileName).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_recognises_xlsx_by_mime_type()
    {
        _sut.CanHandle(
            mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName: "unknown")
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_rejects_unrelated_files()
    {
        _sut.CanHandle(mimeType: "application/pdf", fileName: "doc.pdf").Should().BeFalse();
        _sut.CanHandle(mimeType: null, fileName: "notes.txt").Should().BeFalse();
    }

    [Fact]
    public async Task Extracts_text_from_shared_string_cells()
    {
        // A realistic xlsx stores most text in a shared string table and references it
        // by index from each cell. Build one synthetic sheet and confirm the extractor
        // resolves the references rather than emitting bare integers.
        using var stream = BuildSyntheticXlsx(new[] { "Banking Statement", "IBAN GB12", "Closing balance" });

        var text = await _sut.ExtractAsync(stream, maxChars: 5000, TestContext.Current.CancellationToken);

        text.Should().Contain("Banking Statement");
        text.Should().Contain("IBAN GB12");
        text.Should().Contain("Closing balance");
    }

    [Fact]
    public async Task Respects_maxChars_cap()
    {
        using var stream = BuildSyntheticXlsx(new[] { new string('A', 100), new string('B', 100), new string('C', 100) });

        var text = await _sut.ExtractAsync(stream, maxChars: 50, TestContext.Current.CancellationToken);

        text.Length.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task Works_on_non_seekable_stream()
    {
        // Simulates a network download where the incoming stream can't be seeked.
        using var seekable = BuildSyntheticXlsx(new[] { "payroll" });
        using var nonSeekable = new NonSeekableStream(seekable);

        var text = await _sut.ExtractAsync(nonSeekable, maxChars: 1000, TestContext.Current.CancellationToken);

        text.Should().Contain("payroll");
    }

    /// <summary>
    /// Builds a minimal valid xlsx in memory with the given strings stored as shared-string
    /// cells on a single worksheet. Returns a seekable MemoryStream positioned at 0.
    /// </summary>
    private static MemoryStream BuildSyntheticXlsx(string[] values)
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            // Shared string table: the canonical storage for text in xlsx.
            var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedStringPart.SharedStringTable = new SharedStringTable();
            foreach (var value in values)
                sharedStringPart.SharedStringTable.AppendChild(new SharedStringItem(new Text(value)));

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            for (var i = 0; i < values.Length; i++)
            {
                sheetData.AppendChild(new Row(
                    new Cell
                    {
                        CellReference = $"A{i + 1}",
                        DataType = CellValues.SharedString,
                        CellValue = new CellValue(i.ToString()),
                    }));
            }
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        ms.Position = 0;
        return ms;
    }

    /// <summary>Wraps a seekable stream to make it appear non-seekable for the buffering code path.</summary>
    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream _inner;
        public NonSeekableStream(Stream inner) => _inner = inner;
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
