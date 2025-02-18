using System;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using CleanArchitectureTemplate.Infrastructure.Commons.Interfaces;

namespace CleanArchitectureTemplate.Application.Commons.Bases;

public abstract class BaseGetUseCase<TModel, TDto> : BaseUseCase<TModel, TDto>, IBaseGetUseCase<TModel, TDto>
    where TModel : BaseModel
    where TDto : BaseDto
{
    protected BaseGetUseCase(IBaseRepository<TModel> repository) : base(repository)
    {
    }

    public virtual async Task<List<TDto>> GetAllAsync()
    {
        var entities = await this._repository.GetAllAsync();
        return entities.Select(entity => MapToDTO(entity)).ToList();
    }

    public virtual async Task<TDto> GetOneAsync(Guid id)
    {
        var entity = await this._repository.FindAsync(id) ?? throw new KeyNotFoundException($"{typeof(TModel).Name} not found");
        return MapToDTO(entity);
    }

}
