using Amazon.S3;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Infrastructure.Storage.Common.Factories;
using AppTemplate.Infrastructure.Storage.Common.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Access.Stores;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Inventories;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Scanners;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Transfers;
using AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage;

/// <summary>
/// Composes how the application reaches a file's bytes: the store chosen by
/// <c>Files:Storage:Provider</c>, its inventory and direct-transfer capability, and the malware scanner
/// (here because scanning reads this store).
/// </summary>
public static class StorageModule
{
    /// <summary>
    /// Registers the four content ports and the checks of <c>Files:Categories</c> against the chosen
    /// store and scanning. Only the chosen provider's section is bound and validated at start-up.
    /// </summary>
    public static IServiceCollection AddStorageModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<FilesStorageOptions>()
            .Bind(configuration.GetSection(FilesStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FilesStorageOptions>, FilesStorageOptionsValidator>();

        // Read now because it decides what is registered; an unknown provider is the validator's to refuse.
        var storage = configuration.GetSection(FilesStorageOptions.SectionName).Get<FilesStorageOptions>()
            ?? new FilesStorageOptions();

        if (storage.UsesFileSystem)
        {
            AddFileSystemStore(services, configuration);
        }
        else
        {
            AddS3Store(services, configuration);
        }

        AddScanner(services, configuration);

        // Category rules that depend on this module's choices; the categories are the application's.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<FilesOptions>, FilesCategoriesStorageValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<FilesOptions>, FilesCategoriesScanningValidator>());

        return services;
    }

    private static void AddS3Store(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FilesStorageS3Options>()
            .Bind(configuration.GetSection(FilesStorageS3Options.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FilesStorageS3Options>, FilesStorageS3OptionsValidator>();

        // One client for the process: it owns a connection pool and a retry schedule.
        services.AddSingleton<IAmazonS3>(provider =>
            BucketClientFactory.Create(provider.GetRequiredService<IOptions<FilesStorageS3Options>>().Value));

        // A second client for presigning only, on the public endpoint (a SigV4 URL covers its host).
        services.AddKeyedSingleton<IAmazonS3>(
            BucketClientFactory.SigningClientKey,
            (provider, _) =>
                BucketClientFactory.CreateForSigning(provider.GetRequiredService<IOptions<FilesStorageS3Options>>().Value));

        services.AddScoped<IFileContentStore, S3FileContentStore>();
        services.AddScoped<IFileContentInventory, S3FileContentInventory>();
        services.AddScoped<IFileDirectTransfer, S3FileDirectTransfer>();
    }

    private static void AddFileSystemStore(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FilesStorageFileSystemOptions>()
            .Bind(configuration.GetSection(FilesStorageFileSystemOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FilesStorageFileSystemOptions>, FilesStorageFileSystemOptionsValidator>();

        services.AddScoped<IFileContentStore, FileSystemFileContentStore>();
        services.AddScoped<IFileContentInventory, FileSystemFileContentInventory>();
        services.AddScoped<IFileDirectTransfer, FileSystemFileDirectTransfer>();
    }

    private static void AddScanner(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FilesScanningOptions>()
            .Bind(configuration.GetSection(FilesScanningOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FilesScanningOptions>, FilesScanningOptionsValidator>();

        bool enabled = configuration.GetSection(FilesScanningOptions.SectionName)
            .GetValue<bool>(nameof(FilesScanningOptions.Enabled));

        if (enabled)
        {
            services.AddScoped<IFileContentScanner, ClamAvFileContentScanner>();
        }
        else
        {
            services.AddScoped<IFileContentScanner, NoOpFileContentScanner>();
        }
    }
}
