namespace CA.Application.Commons.Interfaces.UseCase;

public interface IBaseUseCase<TRequest, TResponse>
    where TResponse : class
{
    public Task<TResponse> ExecuteAsync(TRequest request);

}

public interface IBaseUseCase<TResponse>
    where TResponse : class
{
    Task<TResponse> ExecuteAsync();
}