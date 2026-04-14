using CloudScout.Core.Classification.Extraction;
using CloudScout.Core.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudScout.Core.Classification;

public static class ClassificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the text extraction chain, all three classification tiers, the LLM inference
    /// engine, and the pipeline that coordinates them. Tier 3 (LLM) is only active when a model
    /// path is configured in the <c>Llm</c> config section — otherwise it returns empty results
    /// and the scan proceeds on Tier 0 + 1 alone.
    /// </summary>
    public static IServiceCollection AddCloudScoutClassification(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Text extractors registered in order of specificity — the dispatcher picks the first
        // that says CanHandle(); plain text is last so it doesn't shadow structured formats.
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, OpenXmlDocxTextExtractor>();
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<TextExtractionService>();

        // Classification tiers.
        services.AddSingleton<IClassificationTier, Tier0MetadataClassifier>();
        services.AddSingleton<IClassificationTier, Tier1KeywordClassifier>();
        services.AddSingleton<IClassificationTier, Tier3LlmClassifier>();

        // LLM inference backing Tier 3. Config-driven: empty ModelPath = disabled.
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.AddSingleton<ILlmInference, LlamaSharpInference>();

        services.AddSingleton<ClassificationPipeline>();

        return services;
    }
}
