using System;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using CleanArchitectureTemplate.Infrastructure.Commons.Interfaces;

namespace CleanArchitectureTemplate.Application.Commons.Bases;

public abstract class BaseDeleteUseCase<TModel, TDto> : BaseUseCase<TModel, TDto>, IBaseDeleteUseCase<TModel, TDto>
    where TModel : BaseModel
    where TDto : BaseDto
{
    protected BaseDeleteUseCase(IBaseRepository<TModel> repository) : base(repository)
    {
    }

    public virtual async Task<bool> DeleteAsync(Guid id)
    {
        return await _repository.DeleteAsync(id);
    }

}

