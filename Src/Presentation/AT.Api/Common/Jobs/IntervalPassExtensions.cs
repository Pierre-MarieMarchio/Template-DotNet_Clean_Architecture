using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Api.Common.Jobs;

internal static class IntervalPassExtensions
{
    internal static IServiceCollection AddIntervalPass<TCommand>(
        this IServiceCollection services,
        Func<IServiceProvider, IntervalPass> schedule)
        where TCommand : ICommand<Result<int>>, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(schedule);

        services.AddHostedService(provider =>
            ActivatorUtilities.CreateInstance<IntervalPassBackgroundService<TCommand>>(provider, schedule(provider)));

        return services;
    }
}
