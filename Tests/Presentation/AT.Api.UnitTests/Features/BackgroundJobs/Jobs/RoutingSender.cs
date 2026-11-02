using System.Reflection;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.UnitTests.Features.BackgroundJobs.Jobs;

internal sealed class RoutingSender(IServiceProvider services) : ISender
{
    public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default) =>
        Dispatch<TResponse>(typeof(ICommandHandler<,>), command, cancellationToken);

    public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default) =>
        Dispatch<TResponse>(typeof(IQueryHandler<,>), query, cancellationToken);

    public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The use cases of this project are commands and queries.");

    public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A job sends a typed message.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(object message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private ValueTask<TResponse> Dispatch<TResponse>(Type handlerContract, object message, CancellationToken cancellationToken)
    {
        var contract = handlerContract.MakeGenericType(message.GetType(), typeof(TResponse));
        object handler = services.GetRequiredService(contract);

        return (ValueTask<TResponse>)contract.GetMethod("Handle")!.Invoke(
            handler,
            BindingFlags.DoNotWrapExceptions,
            binder: null,
            [message, cancellationToken],
            culture: null)!;
    }
}
