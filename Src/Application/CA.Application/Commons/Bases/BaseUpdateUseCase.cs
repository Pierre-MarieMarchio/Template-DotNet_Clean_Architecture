using CA.Application.Commons.Factories;
using CA.Application.Commons.Interfaces.Repositories;
using CA.Application.Commons.Interfaces.UseCase;
using CA.Domain.Commons.Bases;

namespace CA.Application.Commons.Bases;

public abstract class BaseUpdateUseCase<TEntity, TRequest, TResponse>(IBaseRepository<TEntity> repository) : IBaseUseCase<TRequest, TResponse>, IUseRepositoryUseCase<TEntity>
    where TEntity : AuditableBaseEntity
    where TRequest : notnull
    where TResponse : class
{
    public IBaseRepository<TEntity> Repository => repository;

    public virtual async Task<TResponse> ExecuteAsync(TRequest request)
    {
        var entity = MapToEntity(request);
        var result = await Repository.UpdateAsync(entity);
        var response = ResponseFactory.CreateResponse<TResponse>(result);

        return response;
    }

    protected abstract TEntity MapToEntity(TRequest request);
}
