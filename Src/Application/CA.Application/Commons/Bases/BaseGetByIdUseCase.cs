using CA.Application.Commons.Factories;
using CA.Application.Commons.Interfaces.Repositories;
using CA.Application.Commons.Interfaces.UseCase;
using CA.Domain.Commons.Bases;

namespace CA.Application.Commons.Bases;

public abstract class BaseGetByIdUseCase<TEntity, TRequest, TResponse>(IBaseRepository<TEntity> repository) : IBaseUseCase<TRequest, TResponse>, IUseRepositoryUseCase<TEntity>
    where TEntity : AuditableBaseEntity
    where TRequest : notnull
    where TResponse : class
{
    private readonly IBaseRepository<TEntity> _repository = repository;

    public IBaseRepository<TEntity> Repository => _repository;

    public virtual async Task<TResponse> ExecuteAsync(TRequest request)
    {
        var result = await Repository.GetByIdAsync(request!);
        var response = ResponseFactory.CreateResponse<TResponse>(result);
        return response;
    }
}
