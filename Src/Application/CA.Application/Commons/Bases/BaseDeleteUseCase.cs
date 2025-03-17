using CA.Application.Commons.Factories;
using CA.Application.Commons.Interfaces.Repositories;
using CA.Application.Commons.Interfaces.UseCase;
using CA.Domain.Commons.Bases;

namespace CA.Application.Commons.Bases;

public abstract class BaseDeleteUseCase<TEntity, TRequest, TResponse>(IBaseRepository<TEntity> repository)
    : BaseUseCase<TRequest, TResponse>, IUseRepositoryUseCase<TEntity>
    where TEntity : AuditableBaseEntity
    where TRequest : notnull
    where TResponse : class
{


    public override async Task<TResponse> ExecuteAsync(TRequest request)
    {
        var item = await Repository.GetByIdAsync(request);
        var result = await Repository.DeleteAsync(item);
        var response = ResponseFactory.CreateResponse<TResponse>(result);

        return response;
    }
    public IBaseRepository<TEntity> Repository => repository;
}
