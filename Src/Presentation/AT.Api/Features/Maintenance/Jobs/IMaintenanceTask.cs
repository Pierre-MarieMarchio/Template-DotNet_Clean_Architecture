using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

internal interface IMaintenanceTask
{
    string Label { get; }

    string DisabledConsequence { get; }

    bool Enabled { get; }

    Task<Result<int>> RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken);
}

internal sealed class MaintenanceTask<TCommand>(
    string label,
    string disabledConsequence,
    Func<bool> isEnabled) : IMaintenanceTask
    where TCommand : ICommand<Result<int>>, new()
{
    public string Label => label;

    public string DisabledConsequence => disabledConsequence;

    public bool Enabled => isEnabled();

    public async Task<Result<int>> RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        await scopedServices.GetRequiredService<ISender>().Send(new TCommand(), cancellationToken);
}
