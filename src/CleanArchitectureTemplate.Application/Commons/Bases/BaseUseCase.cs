using System;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using CleanArchitectureTemplate.Infrastructure.Commons.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitectureTemplate.Application.Commons.Bases;

public abstract class BaseUseCase<TModel, TDto> : IBaseUseCase<TModel>
    where TModel : BaseModel
{

    protected readonly IBaseRepository<TModel> _repository;
    IBaseRepository<TModel> IBaseUseCase<TModel>.Repository => _repository;

    protected BaseUseCase(IBaseRepository<TModel> repository)
    {
        _repository = repository;
    }

    protected virtual TDto MapToDTO(TModel entity) => throw new NotImplementedException();
    protected virtual TModel MapToEntity(TDto dto) => throw new NotImplementedException();
    protected virtual void CopyDtoToEntity(TDto dto, TModel entity) => throw new NotImplementedException();

}






