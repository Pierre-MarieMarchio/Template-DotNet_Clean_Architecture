using System;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using CleanArchitectureTemplate.Infrastructure.Commons.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitectureTemplate.Application.Commons.Bases;

public abstract class BasePutUseCase<TModel, TDto> : BaseUseCase<TModel, TDto>, IBasePutUseCase<TModel, TDto>
    where TModel : BaseModel
    where TDto : BaseDto, IEntityWithId<Guid>
{
    protected BasePutUseCase(IBaseRepository<TModel> repository) : base(repository)
    {
    }

    public virtual async Task<TDto> PutAsync(Guid id, TDto entityDTO)
    {
        this.ValidateIdMatch(id, entityDTO.Id);

        var entity = await _repository.FindAsync(id) ?? throw new KeyNotFoundException($"{typeof(TModel).Name} not found");
        CopyDtoToEntity(entityDTO, entity);

        try
        {
            await _repository.UpdateAsync(entity);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DbUpdateConcurrencyException("A concurrency conflict occurred while updating the TodoItem.");
        }

        return MapToDTO(entity);
    }

    protected void ValidateIdMatch(Guid providedId, Guid entityId)
    {
        if (providedId != entityId)
        {
            throw new ArgumentException("The provided ID does not match the ID of the item.");
        } 
    }
}
