using Microsoft.Extensions.DependencyInjection;

namespace CloudScout.Core.Taxonomy;

public static class TaxonomyServiceCollectionExtensions
{
    public static IServiceCollection AddCloudScoutTaxonomy(this IServiceCollection services)
    {
        services.AddSingleton<ITaxonomyProvider, EmbeddedAndFileTaxonomyProvider>();
        return services;
    }
}
