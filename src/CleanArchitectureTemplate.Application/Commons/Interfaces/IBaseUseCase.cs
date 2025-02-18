using System;
using CleanArchitectureTemplate.Application.Commons.Bases;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using CleanArchitectureTemplate.Infrastructure.Commons.Interfaces;
using FluentValidation;

namespace CleanArchitectureTemplate.Application.Commons.Interfaces;

public interface IBaseUseCase<TModel>
    where TModel : BaseModel
{
    IBaseRepository<TModel> Repository { get; }
}

public interface IBaseGetUseCase<TModel, TDto> : IBaseUseCase<TModel>
    where TModel : BaseModel
    where TDto : BaseDto
{
    Task<List<TDto>> GetAllAsync();
    Task<TDto> GetOneAsync(Guid id);
}

public interface IBasePostUseCase<TModel, TDto> : IBaseUseCase<TModel>
    where TModel : BaseModel
    where TDto : BaseDto
{
    IValidator<TDto> Validator { get; }
    Task<TDto> PostAsync(TDto entityDTO);
}

public interface IBasePutUseCase<TModel, TDto> : IBaseUseCase<TModel>
    where TModel : BaseModel
    where TDto : BaseDto
{
    Task<TDto> PutAsync(Guid id, TDto entityDTO);
}

public interface IBaseDeleteUseCase<TModel, TDto> : IBaseUseCase<TModel>
    where TModel : BaseModel
{
    Task<bool> DeleteAsync(Guid id);
}