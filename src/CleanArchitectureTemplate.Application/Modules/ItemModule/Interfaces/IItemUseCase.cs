using System;
using CleanArchitectureTemplate.Application.Commons.Bases;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using FluentValidation;


namespace CleanArchitectureTemplate.Application.Modules.ItemModule.Interfaces;

public interface IItemGetUseCase<T, TDto> : IBaseGetUseCase<T, TDto>
    where T : BaseModel
    where TDto : BaseDto
{

}

public interface IItemPostUseCase<T, TDto> : IBasePostUseCase<T, TDto>
    where T : BaseModel
    where TDto : BaseDto
{

}

public interface IItemPutUseCase<T, TDto> : IBasePutUseCase<T, TDto>
    where T : BaseModel
    where TDto : BaseDto
{

}

public interface IItemDeleteUseCase<T, TDto> : IBaseDeleteUseCase<T, TDto>
    where T : BaseModel
    where TDto : BaseDto
{

}