using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

/// <summary>
/// One purge the maintenance timer runs: which use case, what to call it, and whether it is on.
/// <para>
/// A feature that owns a purge registers its own, so the timer holds no list of purges and removing
/// that feature removes its task without editing the timer or the composition that starts it.
/// </para>
/// </summary>
internal interface IMaintenanceTask
{
    /// <summary>What the task is called in logs, spans and the <c>task</c> tag of its counters.</summary>
    string Label { get; }

    /// <summary>What accumulates while the task is switched off, said on every skipped iteration.</summary>
    string DisabledConsequence { get; }

    bool Enabled { get; }

    /// <summary>Runs the use case from a scope the timer opened for this iteration.</summary>
    Task<Result<int>> RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken);
}

internal sealed class MaintenanceTask<TUseCase>(
    string label,
    string disabledConsequence,
    Func<bool> isEnabled) : IMaintenanceTask
    where TUseCase : IUseCase<Result<int>>
{
    public string Label => label;

    public string DisabledConsequence => disabledConsequence;

    public bool Enabled => isEnabled();

    public Task<Result<int>> RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        scopedServices.GetRequiredService<TUseCase>().ExecuteAsync(cancellationToken);
}
