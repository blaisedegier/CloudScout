using CloudScout.Core.Classification.Extraction;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Drawing;
using FluentAssertions;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace CloudScout.Core.Tests.Classification.Extraction;

public class OpenXmlPptxTextExtractorTests
{
    private readonly OpenXmlPptxTextExtractor _sut = new();

    [Theory]
    [InlineData("deck.pptx")]
    [InlineData("QUARTERLY.PPTX")]
    [InlineData("macros.pptm")]
    public void CanHandle_recognises_pptx_by_extension(string fileName)
    {
        _sut.CanHandle(mimeType: null, fileName: fileName).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_recognises_pptx_by_mime_type()
    {
        _sut.CanHandle(
            mimeType: "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            fileName: "unknown")
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_rejects_unrelated_files()
    {
        _sut.CanHandle(mimeType: "application/pdf", fileName: "doc.pdf").Should().BeFalse();
        _sut.CanHandle(mimeType: null, fileName: "data.xlsx").Should().BeFalse();
    }

    [Fact]
    public async Task Extracts_text_across_slides()
    {
        using var stream = BuildSyntheticPptx(new[]
        {
            "Quarterly Earnings 2026",
            "Revenue up 18 percent",
            "Outlook for next quarter",
        });

        var text = await _sut.ExtractAsync(stream, maxChars: 5000);

        text.Should().Contain("Quarterly Earnings 2026");
        text.Should().Contain("Revenue up 18 percent");
        text.Should().Contain("Outlook for next quarter");
    }

    [Fact]
    public async Task Respects_maxChars_cap()
    {
        using var stream = BuildSyntheticPptx(new[]
        {
            new string('A', 200),
            new string('B', 200),
        });

        var text = await _sut.ExtractAsync(stream, maxChars: 80);

        text.Length.Should().BeLessThanOrEqualTo(80);
    }

    /// <summary>
    /// Builds a minimal valid .pptx with one text paragraph per slide. Returns a seekable MemoryStream.
    /// </summary>
    private static MemoryStream BuildSyntheticPptx(string[] slideTexts)
    {
        var ms = new MemoryStream();
        using (var doc = PresentationDocument.Create(ms, PresentationDocumentType.Presentation))
        {
            var presPart = doc.AddPresentationPart();
            presPart.Presentation = new P.Presentation();

            // A slide master and layout are required by the schema — build minimal ones.
            var slideMasterPart = presPart.AddNewPart<SlideMasterPart>();
            slideMasterPart.SlideMaster = new SlideMaster(
                new CommonSlideData(new ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties())),
                new P.ColorMap { Background1 = D.ColorSchemeIndexValues.Light1, Text1 = D.ColorSchemeIndexValues.Dark1, Background2 = D.ColorSchemeIndexValues.Light2, Text2 = D.ColorSchemeIndexValues.Dark2, Accent1 = D.ColorSchemeIndexValues.Accent1, Accent2 = D.ColorSchemeIndexValues.Accent2, Accent3 = D.ColorSchemeIndexValues.Accent3, Accent4 = D.ColorSchemeIndexValues.Accent4, Accent5 = D.ColorSchemeIndexValues.Accent5, Accent6 = D.ColorSchemeIndexValues.Accent6, Hyperlink = D.ColorSchemeIndexValues.Hyperlink, FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink });

            var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
            slideLayoutPart.SlideLayout = new SlideLayout(
                new CommonSlideData(new ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties())),
                new ColorMapOverride(new D.MasterColorMapping()));

            var slideIdList = new SlideIdList();
            uint slideIdSeed = 256U;

            foreach (var text in slideTexts)
            {
                var slidePart = presPart.AddNewPart<SlidePart>();
                slidePart.Slide = new Slide(
                    new CommonSlideData(new ShapeTree(
                        new P.NonVisualGroupShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                            new P.NonVisualGroupShapeDrawingProperties(),
                            new ApplicationNonVisualDrawingProperties()),
                        new GroupShapeProperties(),
                        new P.Shape(
                            new P.NonVisualShapeProperties(
                                new P.NonVisualDrawingProperties { Id = 2U, Name = "Text" },
                                new P.NonVisualShapeDrawingProperties(),
                                new ApplicationNonVisualDrawingProperties()),
                            new P.ShapeProperties(),
                            new P.TextBody(
                                new BodyProperties(),
                                new ListStyle(),
                                new Paragraph(new Run(new D.Text(text))))))));
                slidePart.AddPart(slideLayoutPart, "rId1");

                slideIdList.Append(new SlideId { Id = slideIdSeed++, RelationshipId = presPart.GetIdOfPart(slidePart) });
            }

            presPart.Presentation.Append(
                new SlideMasterIdList(new SlideMasterId { Id = 2147483648U, RelationshipId = presPart.GetIdOfPart(slideMasterPart) }),
                slideIdList,
                new SlideSize { Cx = 9144000, Cy = 6858000 },
                new NotesSize { Cx = 6858000, Cy = 9144000 });

            presPart.AddPart(slideMasterPart, presPart.GetIdOfPart(slideMasterPart));
        }

        ms.Position = 0;
        return ms;
    }
}
