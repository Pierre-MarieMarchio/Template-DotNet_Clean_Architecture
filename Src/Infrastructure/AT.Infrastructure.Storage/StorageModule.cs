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

public static class StorageModule
{
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

        services.AddSingleton<IAmazonS3>(provider =>
            BucketClientFactory.Create(provider.GetRequiredService<IOptions<FilesStorageS3Options>>().Value));

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
