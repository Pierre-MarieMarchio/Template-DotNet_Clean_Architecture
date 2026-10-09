using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Files.Jobs;

internal static class FileJobsExtensions
{
    /// <summary>
    /// Binds and validates <see cref="FileWorkerOptions"/> and starts the three file passes.
    /// </summary>
    internal static IServiceCollection AddFileJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<FileWorkerOptions>()
            .Bind(configuration.GetSection(FileWorkerOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<FileWorkerOptions>, FileWorkerOptionsValidator>();

        services.AddHostedService<FileBackgroundService>();

        return services;
    }
}
