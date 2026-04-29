using CloudScout.Core.Crawling.Authentication;
using CloudScout.Core.Crawling.Dropbox;
using CloudScout.Core.Crawling.GoogleDrive;
using CloudScout.Core.Crawling.OneDrive;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudScout.Core.Crawling;

public static class CrawlingServiceCollectionExtensions
{
    /// <summary>
    /// Registers every <see cref="ICloudStorageProvider"/> and its auth plumbing. Bind the
    /// per-provider options sections from configuration before calling a command that
    /// authenticates or enumerates.
    /// </summary>
    public static IServiceCollection AddCloudScoutCrawling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MicrosoftAuthOptions>(configuration.GetSection(MicrosoftAuthOptions.SectionName));
        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.SectionName));
        services.Configure<DropboxAuthOptions>(configuration.GetSection(DropboxAuthOptions.SectionName));

        services.AddSingleton<MsalPublicClientFactory>();
        services.AddSingleton<GoogleCredentialFactory>();
        services.AddSingleton<DropboxCredentialFactory>();

        services.AddSingleton<ICloudStorageProvider, OneDriveProvider>();
        services.AddSingleton<ICloudStorageProvider, GoogleDriveProvider>();
        services.AddSingleton<ICloudStorageProvider, DropboxProvider>();
        return services;
    }
}
