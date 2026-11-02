using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

internal interface IMaintenanceTask
{
    string Label { get; }

    string DisabledConsequence { get; }

    bool Enabled { get; }

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
