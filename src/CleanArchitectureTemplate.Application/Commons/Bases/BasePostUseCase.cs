using System;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Application.Commons.Services;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using CleanArchitectureTemplate.Infrastructure.Commons.Interfaces;
using FluentValidation;

namespace CleanArchitectureTemplate.Application.Commons.Bases;

public abstract class BasePostUseCase<TModel, TDto> : BaseUseCase<TModel, TDto>, IBasePostUseCase<TModel, TDto>
    where TModel : BaseModel
    where TDto : BaseDto
{

    protected readonly IValidator<TDto> _validator;
    IValidator<TDto> IBasePostUseCase<TModel, TDto>.Validator => _validator;
    protected BasePostUseCase(IBaseRepository<TModel> repository, IValidator<TDto> validator) : base(repository)
    {
        _validator = validator;
    }

    public virtual async Task<TDto> PostAsync(TDto entityDTO)
    {
        await ValidationService.Validate(this._validator!, entityDTO);

        var entity = MapToEntity(entityDTO);
        var createdEntity = await this._repository.AddAsync(entity);

        return MapToDTO(createdEntity);
    }
}
