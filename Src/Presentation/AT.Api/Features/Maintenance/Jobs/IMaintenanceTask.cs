using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

internal interface IMaintenanceTask
{
    JobPass Pass { get; }

    bool Enabled { get; }

    Task SendAsync(IServiceProvider scopedServices, ILogger logger, CancellationToken cancellationToken);
}

internal sealed class MaintenanceTask<TCommand>(JobPass pass, Func<bool> isEnabled) : IMaintenanceTask
    where TCommand : ICommand<Result<int>>, new()
{
    public JobPass Pass => pass;

    public bool Enabled => isEnabled();

    public Task SendAsync(IServiceProvider scopedServices, ILogger logger, CancellationToken cancellationToken) =>
        pass.SendAsync<TCommand>(scopedServices, logger, cancellationToken);
}
