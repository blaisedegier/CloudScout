using CloudScout.Core.Classification.Extraction;
using FluentAssertions;
using System.Text;

namespace CloudScout.Core.Tests.Classification.Extraction;

public class RtfTextExtractorTests
{
    private readonly RtfTextExtractor _sut = new();

    [Theory]
    [InlineData("letter.rtf")]
    [InlineData("NOTES.RTF")]
    public void CanHandle_recognises_rtf_by_extension(string fileName)
    {
        _sut.CanHandle(mimeType: null, fileName: fileName).Should().BeTrue();
    }

    [Theory]
    [InlineData("application/rtf")]
    [InlineData("text/rtf")]
    public void CanHandle_recognises_rtf_by_mime_type(string mime)
    {
        _sut.CanHandle(mimeType: mime, fileName: "unknown").Should().BeTrue();
    }

    [Fact]
    public void CanHandle_rejects_unrelated_files()
    {
        _sut.CanHandle(mimeType: "application/pdf", fileName: "doc.pdf").Should().BeFalse();
        _sut.CanHandle(mimeType: null, fileName: "plain.txt").Should().BeFalse();
    }

    [Fact]
    public void Parse_strips_basic_control_words()
    {
        var rtf = @"{\rtf1\ansi\ansicpg1252\deff0 Hello world.}";

        var text = RtfTextExtractor.Parse(rtf, maxChars: 1000);

        text.Should().Contain("Hello world.");
        text.Should().NotContain("rtf1");
        text.Should().NotContain("ansi");
    }

    [Fact]
    public void Parse_handles_escaped_braces_and_backslashes()
    {
        var rtf = @"{\rtf1 File \{confidential\} at path C:\\temp\\a.doc}";

        var text = RtfTextExtractor.Parse(rtf, maxChars: 1000);

        text.Should().Contain("{confidential}");
        text.Should().Contain(@"C:\temp\a.doc");
    }

    [Fact]
    public void Parse_skips_font_and_color_tables()
    {
        // fonttbl and colortbl are metadata groups — their contents should not leak into the output.
        var rtf = @"{\rtf1{\fonttbl{\f0\fnil Arial;}{\f1\fnil Times New Roman;}}" +
                  @"{\colortbl;\red0\green0\blue0;\red255\green255\blue255;}" +
                  @"Visible body text here.}";

        var text = RtfTextExtractor.Parse(rtf, maxChars: 1000);

        text.Should().Contain("Visible body text here.");
        text.Should().NotContain("Arial");
        text.Should().NotContain("Times New Roman");
    }

    [Fact]
    public void Parse_translates_paragraph_breaks_to_whitespace()
    {
        // Without paragraph handling, \par followed immediately by the next paragraph would
        // glue words together, breaking classification keyword matches.
        var rtf = @"{\rtf1 First paragraph.\par Second paragraph.}";

        var text = RtfTextExtractor.Parse(rtf, maxChars: 1000);

        text.Should().Contain("First paragraph.");
        text.Should().Contain("Second paragraph.");
        // Ensure the two paragraphs are separated (not "paragraph.Second")
        text.Should().NotContain("paragraph.Second");
    }

    [Fact]
    public void Parse_respects_maxChars()
    {
        var rtf = @"{\rtf1 " + new string('A', 500) + "}";

        var text = RtfTextExtractor.Parse(rtf, maxChars: 50);

        text.Length.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task ExtractAsync_reads_from_stream()
    {
        var rtf = @"{\rtf1\ansi This is a last will and testament.\par Signed by the testator.}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));

        var text = await _sut.ExtractAsync(stream, maxChars: 1000, TestContext.Current.CancellationToken);

        text.Should().Contain("last will and testament");
        text.Should().Contain("testator");
    }
}
