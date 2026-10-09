using Amazon.S3;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Infrastructure.Storage.Common.Factories;
using AppTemplate.Infrastructure.Storage.Common.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Inventories;
using AppTemplate.Infrastructure.Storage.Features.Files.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Scanners;
using AppTemplate.Infrastructure.Storage.Features.Files.Stores;
using AppTemplate.Infrastructure.Storage.Features.Files.Transfers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage;

/// <summary>
/// Composes how this application reaches a file's bytes: the store chosen by
/// <c>Files:Storage:Provider</c>, its inventory and direct-transfer capability, and the malware
/// scanner. The half of the Files feature that lives in a database is the persistence module's; the
/// two meet only in a use case.
/// <para>
/// The scanner is here because scanning a file means reading this store; a module beside this one
/// would need its own copy of the configuration that reaches it.
/// </para>
/// </summary>
public static class StorageModule
{
    /// <summary>
    /// Registers <see cref="IFileContentStore"/>, <see cref="IFileContentInventory"/>,
    /// <see cref="IFileDirectTransfer"/> and <see cref="IFileContentScanner"/>, and the checks of
    /// <c>Files:Categories</c> against the chosen store and scanning.
    /// </summary>
    /// <param name="services">The container being composed.</param>
    /// <param name="configuration">
    /// Supplies <c>Files:Storage</c> and <c>Files:Scanning</c>. Only the chosen provider's section is
    /// bound and validated at start-up, so a misconfigured store stops the process from booting
    /// rather than failing on the first file.
    /// </param>
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

        // Read now, because it decides what is registered; an unknown provider is refused at start-up
        // by the validator above.
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

        // The category rules that depend on this module's choices; the categories themselves are
        // bound and validated by the application's AddFiles.
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

        // A second client for presigning only, on the public endpoint: a SigV4 URL covers the host it
        // was signed for. It opens no connection.
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
