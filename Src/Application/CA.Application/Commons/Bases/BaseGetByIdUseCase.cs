using CA.Application.Commons.Factories;
using CA.Application.Commons.Interfaces.Repositories;
using CA.Application.Commons.Interfaces.UseCase;
using CA.Domain.Commons.Bases;

namespace CA.Application.Commons.Bases;

public abstract class BaseGetByIdUseCase<TEntity, TRequest, TResponse>(IBaseRepository<TEntity> repository) : BaseUseCase<TRequest, TResponse>, IUseRepositoryUseCase<TEntity>
    where TEntity : AuditableBaseEntity
    where TRequest : notnull
    where TResponse : class
{
    private readonly IBaseRepository<TEntity> _repository = repository;

    public IBaseRepository<TEntity> Repository => _repository;

    public override async Task<TResponse> ExecuteAsync(TRequest id)
    {
        var result = await Repository.GetByIdAsync(id!);
        var response = ResponseFactory.CreateResponse<TResponse>(result);
        return response;
    }
}
