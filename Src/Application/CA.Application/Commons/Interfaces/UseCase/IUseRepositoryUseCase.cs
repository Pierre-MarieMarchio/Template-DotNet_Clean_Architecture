using CA.Application.Commons.Interfaces.Repositories;
using CA.Domain.Commons.Bases;

namespace CA.Application.Commons.Interfaces.UseCase;

public interface IUseRepositoryUseCase<TEntity>
    where TEntity : AuditableBaseEntity
{
    IBaseRepository<TEntity> Repository { get; }
}
