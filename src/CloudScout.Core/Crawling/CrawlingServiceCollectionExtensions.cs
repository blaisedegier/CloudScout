using CloudScout.Core.Crawling.Authentication;
using CloudScout.Core.Crawling.OneDrive;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudScout.Core.Crawling;

public static class CrawlingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICloudStorageProvider"/> implementations and their supporting
    /// authentication plumbing. Bind <see cref="MicrosoftAuthOptions"/> from configuration
    /// before calling a command that authenticates or enumerates.
    /// </summary>
    public static IServiceCollection AddCloudScoutCrawling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MicrosoftAuthOptions>(configuration.GetSection(MicrosoftAuthOptions.SectionName));
        services.AddSingleton<MsalPublicClientFactory>();
        services.AddSingleton<ICloudStorageProvider, OneDriveProvider>();
        return services;
    }
}
