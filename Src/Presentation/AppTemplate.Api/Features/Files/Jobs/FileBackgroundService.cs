using System.Diagnostics;
using System.Diagnostics.Metrics;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using AppTemplate.Presentation.Core.Common.Jobs;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Files.Jobs;

/// <summary>
/// Runs the three file passes (abandoned-registration purge, orphan reclamation and deposit
/// inspection), each on its own timer and option. A failing pass is logged and retried at the next
/// tick; each use case takes its own <see cref="ILeaderLease"/>, so a standby host's pass reports zero.
/// </summary>
internal sealed class FileBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<FileWorkerOptions> options,
    ILogger<FileBackgroundService> logger) : BackgroundService
{
    private const string _abandonedRegistrationsTask = "abandoned registrations";
    private const string _orphanedContentTask = "orphaned content";

    private const string _depositedFilesTask = "deposited files";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (logger.IsEnabled(LogLevel.Information))
        {
            // The abandonment delay is in the line because no setting carries it.
            logger.LogInformation(
                "File worker starting. Abandoned-registration purge enabled: {PurgeEnabled}, every " +
                "{PurgeInterval}, giving up on a registration after {AbandonedAfter}. Orphaned-content " +
                "reclamation enabled: {ReclaimEnabled}, every {ReclaimInterval}.",
                settings.PurgeAbandonedRegistrationsEnabled,
                settings.PurgeAbandonedRegistrationsInterval,
                PurgeAbandonedRegistrationsUseCase.AbandonedAfter,
                settings.ReclaimOrphanedContentEnabled,
                settings.ReclaimOrphanedContentInterval);
        }

        await Task.WhenAll(
            RunLoopAsync<IPurgeAbandonedRegistrationsUseCase>(
                _abandonedRegistrationsTask,
                "Registrations whose deposit never arrived will accumulate, each one holding a " +
                "quota slot its owner never gets back.",
                settings.PurgeAbandonedRegistrationsInterval,
                settings.PurgeAbandonedRegistrationsEnabled,
                FileInstruments.RegistrationsPurged,
                stoppingToken),
            RunLoopAsync<IReclaimOrphanedContentUseCase>(
                _orphanedContentTask,
                "Nothing else reclaims the bytes of a deleted file — the deletion event is a fast " +
                "path, not a guarantee — so stored objects will grow without bound.",
                settings.ReclaimOrphanedContentInterval,
                settings.ReclaimOrphanedContentEnabled,
                FileInstruments.ObjectsReclaimed,
                stoppingToken),
            RunLoopAsync<IInspectDepositedFilesUseCase>(
                _depositedFilesTask,
                "No upload will ever become readable: inspection is the only thing that moves a " +
                "file from deposited to available, so this switch stops the feature rather than " +
                "degrading it.",
                settings.InspectDepositedFilesInterval,
                settings.InspectDepositedFilesEnabled,
                FileInstruments.DepositsInspected,
                stoppingToken));

        logger.LogInformation("File worker stopping.");
    }

    /// <param name="disabledConsequence">What goes wrong while the pass is off, logged at warning level on
    /// every skipped tick: what accumulates fails silently.</param>
    private Task RunLoopAsync<TUseCase>(
        string label,
        string disabledConsequence,
        TimeSpan interval,
        bool enabled,
        Counter<long> volume,
        CancellationToken stoppingToken)
        where TUseCase : IUseCase<Result<int>>
        => PeriodicJob.RunAsync(
            interval,
            async token =>
            {
                if (enabled)
                {
                    await RunPassAsync<TUseCase>(label, volume, token);
                }
                else
                {
                    // Counted so a flat Iterations series tells a disabled loop from a dead one.
                    FileInstruments.Iterations.Add(
                        1,
                        new KeyValuePair<string, object?>(JobTags.Task, label),
                        new KeyValuePair<string, object?>(JobTags.Outcome, "disabled"));

                    logger.LogWarning(
                        "The {Label} sweep is disabled; skipping this pass. {Consequence}",
                        label,
                        disabledConsequence);
                }
            },
            stoppingToken);

    /// <summary>Runs one pass, isolating its failure from the other loops.</summary>
    private async Task RunPassAsync<TUseCase>(string label, Counter<long> volume, CancellationToken stoppingToken)
        where TUseCase : IUseCase<Result<int>>
    {
        using Activity? activity = FileInstruments.ActivitySource.StartActivity("files.sweep");
        activity?.SetTag("files.task", label);

        KeyValuePair<string, object?> taskTag = new(JobTags.Task, label);

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateBackgroundScope();

            var useCase = scope.ServiceProvider.GetRequiredService<TUseCase>();
            Result<int> result = await useCase.ExecuteAsync(stoppingToken);

            if (result.IsSuccess)
            {
                FileInstruments.Iterations.Add(1, taskTag, new(JobTags.Outcome, "success"));
                volume.Add(result.Value, taskTag);
                activity?.SetTag("files.removed", result.Value);

                // Unconditional: a line only on removal would make a broken pass look idle.
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Sweep of {Label} completed: {Count} removed.", label, result.Value);
                }
            }
            else
            {
                Error error = result.Error!;
                FileInstruments.Iterations.Add(1, taskTag, new(JobTags.Outcome, "failure"));
                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "Sweeping {Label} reported a failure: {ErrorCode} — {ErrorMessage}.",
                    label,
                    error.Code,
                    error.Message);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failed pass.
            throw;
        }
        catch (Exception exception)
        {
            FileInstruments.Iterations.Add(1, taskTag, new(JobTags.Outcome, "exception"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            logger.LogError(exception, "Sweeping {Label} failed unexpectedly; will retry at the next interval.", label);
        }
    }
}
