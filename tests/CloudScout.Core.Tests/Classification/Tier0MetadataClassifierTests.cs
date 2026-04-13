using CloudScout.Core.Classification;
using FluentAssertions;

namespace CloudScout.Core.Tests.Classification;

public class Tier0MetadataClassifierTests
{
    private readonly Tier0MetadataClassifier _sut = new();

    private static ClassificationContext ContextFor(string name, string folder = "/", string? mime = null) =>
        new()
        {
            FileName = name,
            ParentFolderPath = folder,
            FullPath = folder == "/" ? $"/{name}" : $"{folder}/{name}",
            MimeType = mime,
        };

    [Fact]
    public async Task Filename_keyword_match_alone_scores_0_6_of_base_confidence()
    {
        // "testament" matches wills filename keyword; no folder, no mime.
        var ctx = ContextFor("Last_Testament.pdf");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed());

        var wills = results.FirstOrDefault(r => r.CategoryId == "wills");
        wills.Should().NotBeNull();
        // 0.6 (filename weight) * 0.9 (base) = 0.54
        wills!.ConfidenceScore.Should().BeApproximately(0.54, 0.001);
        wills.Tier.Should().Be(0);
        wills.Reason.Should().Contain("filename");
    }

    [Fact]
    public async Task Folder_keyword_match_alone_scores_0_4_of_base_confidence()
    {
        var ctx = ContextFor("random.pdf", folder: "/Estate/2024");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed());

        var wills = results.First(r => r.CategoryId == "wills");
        // 0.4 (folder weight) * 0.9 (base) = 0.36
        wills.ConfidenceScore.Should().BeApproximately(0.36, 0.001);
    }

    [Fact]
    public async Task Filename_plus_folder_hit_combines_to_full_base_confidence_when_mime_also_matches()
    {
        // "will" in filename + "estate" folder + pdf mime = full 1.0 weight capped, * 0.9 = 0.9
        var ctx = ContextFor("Will_Final.pdf", folder: "/Estate", mime: "application/pdf");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed());

        var wills = results.First(r => r.CategoryId == "wills");
        wills.ConfidenceScore.Should().BeApproximately(0.9, 0.001);
    }

    [Fact]
    public async Task Negative_keyword_in_filename_excludes_the_category_entirely()
    {
        // "goodwill" is a negative for wills, even though "will" is a substring positive match.
        var ctx = ContextFor("Goodwill_Impairment.pdf");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed());

        results.Should().NotContain(r => r.CategoryId == "wills");
    }

    [Fact]
    public async Task Matching_is_case_insensitive()
    {
        var ctx = ContextFor("WILL_2024.PDF");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed());

        results.Should().Contain(r => r.CategoryId == "wills");
    }

    [Fact]
    public async Task No_signal_match_yields_no_candidates()
    {
        var ctx = ContextFor("untitled.pdf", folder: "/Unsorted");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed());

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Multiple_matching_categories_are_returned_in_descending_confidence_order()
    {
        // "invoice" matches receipts in filename, "bank" matches banking in folder.
        var ctx = ContextFor("Invoice_042.pdf", folder: "/Bank/Imports");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed());

        results.Should().HaveCount(2);
        // receipts: 0.6 * 0.7 = 0.42; banking: 0.4 * 0.8 = 0.32
        results[0].CategoryId.Should().Be("receipts");
        results[1].CategoryId.Should().Be("banking");
        results[0].ConfidenceScore.Should().BeGreaterThan(results[1].ConfidenceScore);
    }
}
