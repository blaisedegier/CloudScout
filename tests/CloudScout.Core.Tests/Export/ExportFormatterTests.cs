using CloudScout.Core.Export;
using FluentAssertions;
using System.Text;
using System.Text.Json;

namespace CloudScout.Core.Tests.Export;

public class ExportFormatterTests
{
    private static string WriteCsv(IEnumerable<ExportRow> rows)
    {
        using var writer = new StringWriter();
        ExportFormatter.WriteCsv(rows, writer);
        return writer.ToString();
    }

    private static async Task<string> WriteJsonAsync(IEnumerable<ExportRow> rows)
    {
        using var stream = new MemoryStream();
        await ExportFormatter.WriteJsonAsync(rows, stream, TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void Csv_quotes_fields_containing_comma()
    {
        var row = new ExportRow("/a,b.pdf", 100, "id", "Display", 0.5, 1, null);
        var csv = WriteCsv([row]);
        csv.Should().Contain("\"/a,b.pdf\"");
    }

    [Fact]
    public void Csv_doubles_internal_quotes()
    {
        var row = new ExportRow("/he said \"hi\".pdf", 100, null, null, null, null, null);
        var csv = WriteCsv([row]);
        csv.Should().Contain("\"/he said \"\"hi\"\".pdf\"");
    }

    [Fact]
    public void Csv_quotes_fields_containing_newline()
    {
        var row = new ExportRow("/a.pdf", 0, null, null, null, null, "line1\nline2");
        var csv = WriteCsv([row]);
        csv.Should().Contain("\"line1\nline2\"");
    }

    [Fact]
    public void Csv_renders_nulls_as_empty_cells()
    {
        var row = new ExportRow("/a.pdf", 42, null, null, null, null, null);
        var csv = WriteCsv([row]);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[1].TrimEnd('\r').Should().Be("/a.pdf,42,,,,,");
    }

    [Fact]
    public void Csv_empty_input_writes_header_only()
    {
        var csv = WriteCsv([]);
        csv.Trim().Should().Be("ExternalPath,SizeBytes,CategoryId,CategoryDisplayName,Confidence,Tier,Reason");
    }

    [Fact]
    public async Task Json_omits_null_properties()
    {
        var row = new ExportRow("/a.pdf", 42, null, null, null, null, null);
        var json = await WriteJsonAsync([row]);
        json.Should().NotContain("CategoryId");
        json.Should().NotContain("Confidence");
        json.Should().Contain("\"ExternalPath\"");
    }

    [Fact]
    public async Task Json_empty_input_writes_empty_array()
    {
        var json = await WriteJsonAsync([]);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }
}
