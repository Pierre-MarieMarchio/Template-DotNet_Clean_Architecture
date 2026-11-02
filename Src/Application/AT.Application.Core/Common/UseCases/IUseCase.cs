namespace AppTemplate.Application.Core.Common.UseCases;

public interface IUseCase;

public interface IUseCase<TRequest, TResponse> : IUseCase
{
    Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);
}

public interface IUseCase<TResponse> : IUseCase
{
    Task<TResponse> ExecuteAsync(CancellationToken cancellationToken = default);
}
