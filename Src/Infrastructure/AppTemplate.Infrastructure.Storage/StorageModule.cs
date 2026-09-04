using Amazon.S3;
using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Common.Factories;
using AppTemplate.Infrastructure.Storage.Common.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Inspectors;
using AppTemplate.Infrastructure.Storage.Features.Files.Inventories;
using AppTemplate.Infrastructure.Storage.Features.Files.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage;

/// <summary>
/// Composes object storage: two options types, two validators, two clients, three adapters.
/// <para>
/// This module has one reason to change — <b>how this application reaches a file's bytes</b>. It is
/// the half of the Files feature that is not in the database, and it shares nothing with the half
/// that is: the aggregate is loaded through a repository in
/// <c>AppTemplate.Infrastructure.Persistence</c>, the bytes are reached through the three ports
/// registered here, and the only place the two meet is a use case. That is what the port boundary
/// buys, and why swapping S3 for another store is a change inside this project.
/// </para>
/// <para>
/// <b>The content inspector is here rather than in a module of its own</b>, and that is the same
/// reason rather than a widening of it: examining a file means opening it, which means this bucket,
/// these credentials and this budget. A module beside this one would need its own copy of all three
/// — only the persistence project may be shared between infrastructure modules — and the seam that
/// would let it borrow them would be an application port no use case consumes, which
/// <c>PortConventionTests</c> refuses. The malware daemon it may also talk to is a collaborator of
/// one adapter, not a second reason for this module to exist.
/// </para>
/// </summary>
public static class StorageModule
{
    /// <summary>
    /// Registers the S3 adapters behind <see cref="IFileContentStore"/>,
    /// <see cref="IFileContentInventory"/> and <see cref="IFileContentInspector"/>.
    /// </summary>
    /// <param name="services">The container being composed.</param>
    /// <param name="configuration">Must supply the <c>Storage</c> section; it is validated at
    /// start-up, so a bucket nobody can sign for stops the process from booting rather than failing
    /// on the first file anyone registers. The <c>ContentInspection</c> section is optional in its
    /// entirety — see <see cref="ContentInspectionOptions"/> for what a deployment that supplies
    /// none of it gets.</param>
    public static IServiceCollection AddStorageModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();

        services.AddOptions<ContentInspectionOptions>()
            .Bind(configuration.GetSection(ContentInspectionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ContentInspectionOptions>, ContentInspectionOptionsValidator>();

        // One client for the process. It is thread-safe, it owns a connection pool and a retry
        // schedule, and a second one would silently double both — so it is a singleton, and the
        // container disposes it at shutdown.
        services.AddSingleton<IAmazonS3>(provider =>
            BucketClientFactory.Create(provider.GetRequiredService<IOptions<StorageOptions>>().Value));

        // The presigning client, and the one place the sentence above does not apply. It exists
        // because Storage:PublicEndpoint may name a host this process never connects to — the
        // browser's name for a store the API reaches under another — and a Signature Version 4 URL
        // covers the host it was signed for, so the name has to be right at signing time rather than
        // rewritten afterwards by anyone. It doubles no pool and no retry schedule: presigning is a
        // keyed hash computed locally and opens no socket at all, which is what makes a second client
        // affordable here and nowhere else in this module. Keyed, because two IAmazonS3 registrations
        // differ by endpoint and by nothing the container could tell apart on its own.
        services.AddKeyedSingleton<IAmazonS3>(
            BucketClientFactory.SigningClientKey,
            (provider, _) =>
                BucketClientFactory.CreateForSigning(provider.GetRequiredService<IOptions<StorageOptions>>().Value));

        services.AddScoped<IFileContentStore, S3FileContentStore>();
        services.AddScoped<IFileContentInventory, S3FileContentInventory>();
        services.AddScoped<IFileContentInspector, S3FileContentInspector>();

        return services;
    }
}
