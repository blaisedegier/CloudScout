using CloudScout.Core.Taxonomy;
using FluentAssertions;

namespace CloudScout.Core.Tests.Taxonomy;

/// <summary>
/// Smoke tests proving that the embedded <c>generic-default</c> taxonomy exists, parses,
/// and passes validation. Rule-by-rule classification behaviour is covered in Tier 0 / Tier 1
/// test files.
/// </summary>
public class TaxonomyLoadingTests
{
    [Fact]
    public async Task Generic_default_taxonomy_loads_from_embedded_resource()
    {
        var provider = new EmbeddedAndFileTaxonomyProvider();

        var taxonomy = await provider.GetAsync("generic-default");

        taxonomy.Name.Should().NotBeNullOrWhiteSpace();
        taxonomy.Version.Should().NotBeNullOrWhiteSpace();
        taxonomy.Categories.Should().NotBeEmpty("the default taxonomy should ship with at least one category");
    }

    [Fact]
    public async Task Generic_default_taxonomy_covers_the_expected_top_level_buckets()
    {
        var provider = new EmbeddedAndFileTaxonomyProvider();
        var taxonomy = await provider.GetAsync("generic-default");

        // The exact set may evolve; lock in the coarse buckets we commit to as product defaults.
        var expectedParents = new[] { "financial", "legal", "identity", "medical", "academic", "vehicle", "personal", "reference" };
        foreach (var id in expectedParents)
            taxonomy.GetById(id).Should().NotBeNull($"'{id}' is one of the documented top-level categories");
    }

    [Fact]
    public async Task Unknown_taxonomy_name_throws_with_a_list_of_available_names()
    {
        var provider = new EmbeddedAndFileTaxonomyProvider();

        var act = async () => await provider.GetAsync("not-a-real-taxonomy");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*generic-default*"); // error message should mention what IS available
    }

    [Fact]
    public async Task Repeated_lookups_are_cached_and_return_the_same_instance()
    {
        var provider = new EmbeddedAndFileTaxonomyProvider();

        var first = await provider.GetAsync("generic-default");
        var second = await provider.GetAsync("generic-default");

        second.Should().BeSameAs(first);
    }
}
