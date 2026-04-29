using CloudScout.Core.Classification;
using FluentAssertions;

namespace CloudScout.Core.Tests.Classification;

public class Tier1KeywordClassifierTests
{
    private readonly Tier1KeywordClassifier _sut = new();

    private static ClassificationContext ContextWithText(string text, string name = "doc.pdf") =>
        new()
        {
            FileName = name,
            ParentFolderPath = "/",
            FullPath = $"/{name}",
            ExtractedText = text,
        };

    [Fact]
    public async Task Empty_extracted_text_produces_no_results()
    {
        var ctx = new ClassificationContext
        {
            FileName = "a.pdf",
            ParentFolderPath = "/",
            FullPath = "/a.pdf",
            ExtractedText = null,
        };

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed(), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Single_content_keyword_match_scores_first_keyword_weight()
    {
        // "iban" is a banking content keyword; no other banking signals.
        var ctx = ContextWithText("Your IBAN is GB12 ABCD 0000.");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed(), TestContext.Current.CancellationToken);

        var banking = results.First(r => r.CategoryId == "banking");
        // First-keyword weight 0.40 * base 0.80 = 0.32
        banking.ConfidenceScore.Should().BeApproximately(0.32, 0.001);
        banking.Tier.Should().Be(1);
    }

    [Fact]
    public async Task Multiple_content_keywords_stack_additional_weight()
    {
        // Both "iban" AND "account number" match.
        var ctx = ContextWithText("IBAN: GB12 ABCD; account number: 12345678");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed(), TestContext.Current.CancellationToken);

        // (0.40 first + 0.10 additional) * 0.80 = 0.40
        var banking = results.First(r => r.CategoryId == "banking");
        banking.ConfidenceScore.Should().BeApproximately(0.40, 0.001);
    }

    [Fact]
    public async Task Content_phrase_weight_is_higher_than_single_keyword()
    {
        var phraseCtx = ContextWithText("This closing balance of the account is positive.");
        var keywordCtx = ContextWithText("Please reference your IBAN on transfers.");

        var ct = TestContext.Current.CancellationToken;
        var phraseResult = (await _sut.ClassifyAsync(phraseCtx, TestTaxonomies.Mixed(), ct)).First(r => r.CategoryId == "banking");
        var keywordResult = (await _sut.ClassifyAsync(keywordCtx, TestTaxonomies.Mixed(), ct)).First(r => r.CategoryId == "banking");

        phraseResult.ConfidenceScore.Should().BeGreaterThan(keywordResult.ConfidenceScore);
    }

    [Fact]
    public async Task Word_boundary_matching_avoids_false_positives()
    {
        // "will" is a filename keyword — but on CONTENT Tier 1 uses word boundaries, so "William"
        // should NOT score as a wills match.
        var ctx = ContextWithText("William and Sarah went to the beach.");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed(), TestContext.Current.CancellationToken);

        results.Should().NotContain(r => r.CategoryId == "wills");
    }

    [Fact]
    public async Task Word_boundary_matching_still_finds_real_matches()
    {
        var ctx = ContextWithText("The testator appointed her executor on 1 Jan 2024.");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed(), TestContext.Current.CancellationToken);

        results.Should().Contain(r => r.CategoryId == "wills");
    }

    [Fact]
    public async Task Matching_is_case_insensitive_for_content()
    {
        var ctx = ContextWithText("IBAN and ACCOUNT NUMBER are both present.");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed(), TestContext.Current.CancellationToken);

        results.Should().Contain(r => r.CategoryId == "banking");
    }

    [Fact]
    public async Task Negative_keyword_in_extracted_text_excludes_the_category()
    {
        // "personal statement" is a banking negative keyword. Even though "closing balance" would
        // normally score banking, the negative excludes it.
        var ctx = ContextWithText("This personal statement describes a closing balance.");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed(), TestContext.Current.CancellationToken);

        results.Should().NotContain(r => r.CategoryId == "banking");
    }

    [Fact]
    public async Task Results_are_ordered_by_descending_confidence()
    {
        // Banking gets phrase + keyword hits; wills gets keyword only.
        var ctx = ContextWithText("Closing balance shown next to the IBAN. Executor noted.");

        var results = await _sut.ClassifyAsync(ctx, TestTaxonomies.Mixed(), TestContext.Current.CancellationToken);

        results.Should().HaveCountGreaterThan(1);
        for (int i = 1; i < results.Count; i++)
            results[i - 1].ConfidenceScore.Should().BeGreaterThanOrEqualTo(results[i].ConfidenceScore);
    }

}
