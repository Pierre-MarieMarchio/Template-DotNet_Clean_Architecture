using System.Diagnostics;
using System.Diagnostics.Metrics;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using AppTemplate.Presentation.Core.Common.Jobs;
using Microsoft.Extensions.Options;

namespace AppTemplate.Worker.Features.Files;

/// <summary>
/// Runs the file feature's three background passes, each on its own timer and its own interval:
/// the two sweeps, <see cref="IPurgeAbandonedRegistrationsUseCase"/> and
/// <see cref="IReclaimOrphanedContentUseCase"/>, and <see cref="IInspectDepositedFilesUseCase"/> —
/// which is not a sweep, and is the one a user feels, since nothing else moves a file from
/// deposited to available. <c>docs/ARCHITECTURE.md</c> carries the argument for three timers rather
/// than one.
/// <para>
/// Each pass takes a fresh scope, is switched on or off by its own option, and is logged and
/// retried at the next tick when it fails. No pass takes <see cref="ILeaderLease"/>: exclusivity
/// between hosts belongs to the operation, and <see cref="ReclaimOrphanedContentUseCase"/> holds
/// the reasoning.
/// </para>
/// <para>
/// <b>Nothing here narrows what the orphan sweep covers, and nothing may.</b> No prefix, no time
/// segment, no memory of where the last pass reached — see
/// <see cref="ReclaimOrphanedContentUseCase"/> for the ordering that makes an unbounded sweep safe
/// and <c>FileWorkerOptions</c> for why no option offered from here may bound it.
/// </para>
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
            // The abandonment delay is in the line because it is configurable nowhere: an operator
            // wondering why a registration is still there after an hour can read what the answer
            // actually is instead of looking for a setting that does not exist.
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

    /// <param name="disabledConsequence">What goes wrong while the sweep is switched off, said on
    /// every skipped pass. Both of these fail silently by construction — what accumulates is rows
    /// and bytes that nothing else in the system will ever remove — so this is logged at warning
    /// level, unlike <c>ReminderBackgroundService</c>'s own disabled skip, where the person waiting
    /// for the reminder is the alarm.</param>
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
                    // Counted, not skipped: an operator reading a flat Iterations series has to be
                    // able to tell a loop switched off from a loop that died, and on this feature
                    // that distinction is sharpest — a stopped inspection loop leaves every upload
                    // permanently unreadable.
                    FileInstruments.Iterations.Add(
                        1,
                        new KeyValuePair<string, object?>("task", label),
                        new KeyValuePair<string, object?>("outcome", "disabled"));

                    logger.LogWarning(
                        "The {Label} sweep is disabled; skipping this pass. {Consequence}",
                        label,
                        disabledConsequence);
                }
            },
            stoppingToken);

    /// <summary>
    /// Runs one sweep and isolates its failure from the other loops: an orphan sweep that cannot
    /// reach the object store must not also stop stale registrations being purged.
    /// </summary>
    private async Task RunPassAsync<TUseCase>(string label, Counter<long> volume, CancellationToken stoppingToken)
        where TUseCase : IUseCase<Result<int>>
    {
        using Activity? activity = FileInstruments.ActivitySource.StartActivity("files.sweep");
        activity?.SetTag("files.task", label);

        KeyValuePair<string, object?> taskTag = new("task", label);

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

            var useCase = scope.ServiceProvider.GetRequiredService<TUseCase>();
            Result<int> result = await useCase.ExecuteAsync(stoppingToken);

            if (result.IsSuccess)
            {
                FileInstruments.Iterations.Add(1, taskTag, new("outcome", "success"));
                volume.Add(result.Value, taskTag);
                activity?.SetTag("files.removed", result.Value);

                // Unconditional: both sweeps report zero for long stretches in a healthy system, so
                // a line that only appeared when something was removed would make a sweep broken for
                // weeks look exactly like one with nothing to do.
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Sweep of {Label} completed: {Count} removed.", label, result.Value);
                }
            }
            else
            {
                Error error = result.Error!;
                FileInstruments.Iterations.Add(1, taskTag, new("outcome", "failure"));
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
            // Shutdown, not a failed sweep: it ends the loop instead of being logged as an error.
            throw;
        }
        catch (Exception exception)
        {
            FileInstruments.Iterations.Add(1, taskTag, new("outcome", "exception"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            logger.LogError(exception, "Sweeping {Label} failed unexpectedly; will retry at the next interval.", label);
        }
    }
}
