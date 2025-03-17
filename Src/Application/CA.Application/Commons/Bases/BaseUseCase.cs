using CA.Application.Commons.Interfaces.UseCase;

namespace CA.Application.Commons.Bases;

public abstract class BaseUseCase<TRequest, TResponse> : IBaseUseCase<TRequest, TResponse>
    where TResponse : class
{
    public abstract Task<TResponse> ExecuteAsync(TRequest request);
}

public abstract class BaseUseCase<TResponse> : IBaseUseCase<TResponse>
    where TResponse : class
{
    public abstract Task<TResponse> ExecuteAsync();
}