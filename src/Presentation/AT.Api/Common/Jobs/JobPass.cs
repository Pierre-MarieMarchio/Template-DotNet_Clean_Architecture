using System.Diagnostics;
using System.Diagnostics.Metrics;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Api.Common.Jobs;

internal sealed record JobPass(
    string Label,
    string DisabledConsequence,
    ActivitySource ActivitySource,
    string ActivityName,
    Counter<long> Iterations,
    Counter<long> Volume)
{
    private KeyValuePair<string, object?> TaskTag => new(JobTags.Task, Label);

    public void SkipDisabled(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        Iterations.Add(1, TaskTag, new(JobTags.Outcome, "disabled"));

        logger.LogWarning(
            "The pass over {Label} is disabled; skipping it. {Consequence}",
            Label,
            DisabledConsequence);
    }

    public async Task SendAsync<TCommand>(
        IServiceProvider scopedServices,
        ILogger logger,
        CancellationToken stoppingToken)
        where TCommand : ICommand<Result<int>>, new()
    {
        ArgumentNullException.ThrowIfNull(scopedServices);
        ArgumentNullException.ThrowIfNull(logger);

        using Activity? activity = ActivitySource.StartActivity(ActivityName);
        activity?.SetTag(JobTags.Task, Label);

        try
        {
            var sender = scopedServices.GetRequiredService<ISender>();
            Result<int> result = await sender.Send(new TCommand(), stoppingToken);

            if (result.IsSuccess)
            {
                Iterations.Add(1, TaskTag, new(JobTags.Outcome, "success"));
                Volume.Add(result.Value, TaskTag);
                activity?.SetTag(JobTags.Count, result.Value);

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("The pass over {Label} completed: {Count} handled.", Label, result.Value);
                }
            }
            else
            {
                Error error = result.Error!;
                Iterations.Add(1, TaskTag, new(JobTags.Outcome, "failure"));
                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "The pass over {Label} reported a failure: {ErrorCode} — {ErrorMessage}.",
                    Label,
                    error.Code,
                    error.Message);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Iterations.Add(1, TaskTag, new(JobTags.Outcome, "exception"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            logger.LogError(
                exception,
                "The pass over {Label} failed unexpectedly; will retry at the next interval.",
                Label);
        }
    }
}
