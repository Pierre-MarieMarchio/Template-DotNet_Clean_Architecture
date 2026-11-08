using System.Diagnostics.Metrics;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Files.Jobs;

internal static class FileJobsExtensions
{
    private static readonly JobPass _abandonedRegistrations = Sweep(
        "abandoned registrations",
        "Registrations whose deposit never arrived will accumulate, each one holding a quota slot " +
        "its owner never gets back.",
        FileInstruments.RegistrationsPurged);

    private static readonly JobPass _orphanedContent = Sweep(
        "orphaned content",
        "Nothing else reclaims the bytes of a deleted file — the deletion event is a fast path, not " +
        "a guarantee — so stored objects will grow without bound.",
        FileInstruments.ObjectsReclaimed);

    private static readonly JobPass _depositedFiles = Sweep(
        "deposited files",
        "No upload will ever become readable: inspection is the only thing that moves a file from " +
        "deposited to available, so this switch stops the feature rather than degrading it.",
        FileInstruments.DepositsInspected);

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

        services.AddIntervalPass<PurgeAbandonedRegistrationsCommand>(provider =>
        {
            var settings = SettingsOf(provider);

            return new IntervalPass(
                _abandonedRegistrations,
                settings.PurgeAbandonedRegistrationsInterval,
                settings.PurgeAbandonedRegistrationsEnabled);
        });

        services.AddIntervalPass<ReclaimOrphanedContentCommand>(provider =>
        {
            var settings = SettingsOf(provider);

            return new IntervalPass(
                _orphanedContent,
                settings.ReclaimOrphanedContentInterval,
                settings.ReclaimOrphanedContentEnabled);
        });

        services.AddIntervalPass<InspectDepositedFilesCommand>(provider =>
        {
            var settings = SettingsOf(provider);

            return new IntervalPass(
                _depositedFiles,
                settings.InspectDepositedFilesInterval,
                settings.InspectDepositedFilesEnabled);
        });

        return services;
    }

    private static JobsFilesOptions SettingsOf(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<JobsFilesOptions>>().Value;

    private static JobPass Sweep(string label, string disabledConsequence, Counter<long> volume) =>
        new(label, disabledConsequence, FileInstruments.ActivitySource, "files.sweep", FileInstruments.Iterations, volume);
}
