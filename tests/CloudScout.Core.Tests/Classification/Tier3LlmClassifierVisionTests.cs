using CloudScout.Core.Classification;
using CloudScout.Core.Inference;
using CloudScout.Core.Taxonomy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CloudScout.Core.Tests.Classification;

/// <summary>
/// Verifies the conditional handoff of image bytes from the orchestrator-populated
/// <see cref="ClassificationContext"/> to the LLM. Image bytes should reach the inference
/// layer for raster image MIMEs, and only those — sending raw PDF or DOCX bytes to a vision
/// model is wasteful and the projector can't interpret them.
/// </summary>
public class Tier3LlmClassifierVisionTests
{
    [Fact]
    public async Task Image_jpeg_with_source_bytes_forwards_to_inference()
    {
        var fakeInference = new RecordingInference();
        var sut = new Tier3LlmClassifier(fakeInference, NullLogger<Tier3LlmClassifier>.Instance);

        var context = new ClassificationContext
        {
            FileName = "scan.jpg",
            ParentFolderPath = "/Identity",
            FullPath = "/Identity/scan.jpg",
            MimeType = "image/jpeg",
            SourceBytes = new byte[] { 0xFF, 0xD8, 0xFF },
        };

        await sut.ClassifyAsync(context, MakeTaxonomy(), TestContext.Current.CancellationToken);

        fakeInference.LastImageBytes.Should().NotBeNull();
        fakeInference.LastImageMimeType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task Pdf_with_source_bytes_does_not_forward_to_inference_as_image()
    {
        // The orchestrator never populates SourceBytes for PDFs (it extracts text instead),
        // but if it ever did, Tier 3 must not treat them as images — the projector would
        // try to decode raw PDF bytes as a raster image and fail.
        var fakeInference = new RecordingInference();
        var sut = new Tier3LlmClassifier(fakeInference, NullLogger<Tier3LlmClassifier>.Instance);

        var context = new ClassificationContext
        {
            FileName = "report.pdf",
            ParentFolderPath = "/",
            FullPath = "/report.pdf",
            MimeType = "application/pdf",
            SourceBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 },
            ExtractedText = "some text",
        };

        await sut.ClassifyAsync(context, MakeTaxonomy(), TestContext.Current.CancellationToken);

        fakeInference.LastImageBytes.Should().BeNull();
        fakeInference.LastImageMimeType.Should().BeNull();
    }

    [Fact]
    public async Task Image_with_no_bytes_loaded_does_not_forward_to_inference_as_image()
    {
        // Defensive: orchestrator might fail to download. Tier 3 still runs (on metadata only)
        // but mustn't claim to have an image when bytes are absent.
        var fakeInference = new RecordingInference();
        var sut = new Tier3LlmClassifier(fakeInference, NullLogger<Tier3LlmClassifier>.Instance);

        var context = new ClassificationContext
        {
            FileName = "lost.png",
            ParentFolderPath = "/",
            FullPath = "/lost.png",
            MimeType = "image/png",
            SourceBytes = null,
        };

        await sut.ClassifyAsync(context, MakeTaxonomy(), TestContext.Current.CancellationToken);

        fakeInference.LastImageBytes.Should().BeNull();
    }

    private static TaxonomyDefinition MakeTaxonomy() => new(
        name: "Test",
        version: "1.0",
        categories: new List<TaxonomyCategory>
        {
            new() { Id = "id.passport", DisplayName = "Passport", ParentId = "id" },
            new() { Id = "id", DisplayName = "Identity" },
        });

    /// <summary>
    /// Spy <see cref="ILlmInference"/> implementation that records what it was called with and
    /// returns a fixed valid-looking response so Tier3LlmClassifier doesn't bail early.
    /// </summary>
    private sealed class RecordingInference : ILlmInference
    {
        public byte[]? LastImageBytes { get; private set; }
        public string? LastImageMimeType { get; private set; }
        public bool IsAvailable => true;

        public Task<string> GenerateAsync(
            string prompt,
            byte[]? imageBytes = null,
            string? imageMimeType = null,
            CancellationToken cancellationToken = default)
        {
            LastImageBytes = imageBytes;
            LastImageMimeType = imageMimeType;
            return Task.FromResult("{\"categoryId\":\"id.passport\",\"confidence\":0.9,\"reason\":\"identity doc\"}");
        }
    }
}
