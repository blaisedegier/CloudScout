using CloudScout.Core.Classification.Extraction;
using Microsoft.Extensions.DependencyInjection;

namespace CloudScout.Core.Classification;

public static class ClassificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the text extraction chain, the classification tiers available in this phase,
    /// and the pipeline that coordinates them. Phase B registers Tier 0 (metadata) and Tier 1
    /// (keyword on extracted text); Phase C will additionally register Tier 3 (LLM).
    /// </summary>
    public static IServiceCollection AddCloudScoutClassification(this IServiceCollection services)
    {
        // Text extractors registered in order of specificity — the dispatcher picks the first
        // that says CanHandle(); plain text is last so it doesn't shadow structured formats.
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, OpenXmlDocxTextExtractor>();
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<TextExtractionService>();

        // Classification tiers. Additional tiers (Tier 3 LLM) will plug in here in Phase C.
        services.AddSingleton<IClassificationTier, Tier0MetadataClassifier>();
        services.AddSingleton<IClassificationTier, Tier1KeywordClassifier>();

        services.AddSingleton<ClassificationPipeline>();

        return services;
    }
}
