using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Files.Jobs;

internal static class FileJobsExtensions
{
    /// <summary>
    /// Binds and validates <see cref="JobsFilesOptions"/> and starts the three file passes.
    /// </summary>
    internal static IServiceCollection AddFileJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<JobsFilesOptions>()
            .Bind(configuration.GetSection(JobsFilesOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<JobsFilesOptions>, JobsFilesOptionsValidator>();

        services.AddHostedService<FileBackgroundService>();

        return services;
    }
}
