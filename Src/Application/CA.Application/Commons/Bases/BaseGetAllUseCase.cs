using CA.Application.Commons.Factories;
using CA.Application.Commons.Interfaces.Repositories;
using CA.Application.Commons.Interfaces.UseCase;
using CA.Domain.Commons.Bases;

namespace CA.Application.Commons.Bases;

public abstract class BaseGetAllUseCase<TEntity, TResponse>(IBaseRepository<TEntity> repository) : BaseUseCase<List<TResponse>>, IUseRepositoryUseCase<TEntity>
    where TEntity : AuditableBaseEntity
    where TResponse : class
{
    public IBaseRepository<TEntity> Repository => repository;

    public override async Task<List<TResponse>> ExecuteAsync()
    {
        var result = await Repository.GetAllAsync();
        var response = ResponseFactory.CreateResponseList<TResponse>(result);
        return response;
    }
}
