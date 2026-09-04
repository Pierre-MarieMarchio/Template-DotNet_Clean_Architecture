using System.Diagnostics;
using System.Diagnostics.Metrics;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using Microsoft.Extensions.Options;

namespace AppTemplate.Worker.Features.Files;

/// <summary>
/// Runs the file feature's three background passes, each on its own timer: its two sweeps,
/// <see cref="IPurgeAbandonedRegistrationsUseCase"/> and <see cref="IReclaimOrphanedContentUseCase"/>,
/// and <see cref="IInspectDepositedFilesUseCase"/> — which is not a sweep and is the one a user
/// feels, since nothing else moves a file from deposited to available. Each pass takes a fresh
/// scope, honours the stopping token rather than waiting it out, and is logged and retried at the
/// next tick when it fails instead of bringing the host down. <c>docs/ARCHITECTURE.md</c> carries
/// the argument for three timers rather than one.
/// <para>
/// No loop can overlap <em>itself</em>: <see cref="PeriodicTimer"/> coalesces the ticks that elapse
/// while a pass is still running. Nothing coalesces two hosts, and no loop here takes
/// <see cref="ILeaderLease"/>: exclusivity between hosts belongs to the operation rather than to the
/// timer that starts it, since a guard placed here would protect this host's callers and nobody
/// else while both use cases stay reachable by other routes. Both have written their answer down —
/// the purge issues idempotent deletes over a range already covered, and deleting the same object
/// twice is deleting it once. If the duplicated <em>listing</em> of a large store ever becomes the
/// cost that matters, the lease belongs inside <see cref="ReclaimOrphanedContentUseCase"/>, next to
/// the reasoning it would contradict.
/// </para>
/// <para>
/// <b>Nothing here narrows what the orphan sweep covers, and nothing may.</b> No prefix, no time
/// segment, no memory of where the last pass reached — see
/// <see cref="ReclaimOrphanedContentUseCase"/> for the ordering argument that makes the sweep safe
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

        try
        {
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
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown. Caught here rather than left to unwind so the line below always runs: an
            // operator watching for a loop that stopped needs the one stop that was asked for to
            // look different from the ones that were not.
        }

        logger.LogInformation("File worker stopping.");
    }

    /// <summary>
    /// Turns the timer's cancellation-on-shutdown into a plain "stop looping" rather than letting
    /// the exception unwind through the do/while below mid-pass — the timer itself never fires
    /// mid-task, but treating its own cancellation as an ordinary false keeps the shutdown path in
    /// this one place instead of scattered across every caller.
    /// </summary>
    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <param name="disabledConsequence">What goes wrong while the sweep is switched off, said on
    /// every skipped pass. Both of these fail silently by construction — what accumulates is rows
    /// and bytes that nothing else in the system will ever remove — so this is logged at warning
    /// level, unlike <c>ReminderBackgroundService</c>'s own disabled skip, where the person waiting
    /// for the reminder is the alarm.</param>
    private async Task RunLoopAsync<TUseCase>(
        string label,
        string disabledConsequence,
        TimeSpan interval,
        bool enabled,
        Counter<long> volume,
        CancellationToken stoppingToken)
        where TUseCase : IUseCase<Result<int>>
    {
        using var timer = new PeriodicTimer(interval);

        do
        {
            if (enabled)
            {
                await RunPassAsync<TUseCase>(label, volume, stoppingToken);
            }
            else
            {
                // Counted, not skipped, for the reason ReminderBackgroundService counts its own
                // disabled pass: an operator reading a flat Iterations series has to be able to tell
                // a loop switched off from a loop that died, and on this feature that distinction is
                // sharpest — a stopped inspection loop leaves every upload permanently unreadable.
                FileInstruments.Iterations.Add(
                    1,
                    new KeyValuePair<string, object?>("task", label),
                    new KeyValuePair<string, object?>("outcome", "disabled"));

                logger.LogWarning(
                    "The {Label} sweep is disabled; skipping this pass. {Consequence}",
                    label,
                    disabledConsequence);
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    /// <summary>
    /// Runs one sweep and isolates its failure from the other loop, the same way
    /// <c>MaintenanceBackgroundService</c> isolates one purge from its sibling: an orphan sweep that
    /// cannot reach the object store must not also stop stale registrations being purged.
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

                // Unconditional on purpose: both sweeps report zero for long stretches in a healthy
                // system, so a line that only appeared when something was removed would make a sweep
                // broken for weeks look exactly like one with nothing to do. The counter above says
                // the same thing to an alert.
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
            // Shutdown, not a failed sweep: let it propagate so the loop stops cleanly instead of
            // logging every graceful shutdown as an error.
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
