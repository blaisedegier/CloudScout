using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudScout.Core.Export;

public static class ExportFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Task WriteJsonAsync(IEnumerable<ExportRow> rows, Stream destination, CancellationToken cancellationToken = default)
        => JsonSerializer.SerializeAsync(destination, rows, JsonOptions, cancellationToken);

    public static void WriteCsv(IEnumerable<ExportRow> rows, TextWriter writer)
    {
        writer.WriteLine("ExternalPath,SizeBytes,CategoryId,CategoryDisplayName,Confidence,Tier,Reason");
        foreach (var row in rows)
        {
            writer.Write(Escape(row.ExternalPath));
            writer.Write(',');
            writer.Write(row.SizeBytes.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(Escape(row.CategoryId));
            writer.Write(',');
            writer.Write(Escape(row.CategoryDisplayName));
            writer.Write(',');
            writer.Write(row.Confidence?.ToString("G17", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(row.Tier?.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(Escape(row.Reason));
            writer.WriteLine();
        }
    }

    private static string Escape(string? value)
    {
        if (value is null) return string.Empty;
        if (value.IndexOfAny(['"', ',', '\r', '\n']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
