using System;
using CleanArchitectureTemplate.Application.Commons.Bases;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureTemplate.API.Commons.Bases;

[Route("api/[controller]")]
[ApiController]
public abstract class BasePostController<TModel, TDto> : ControllerBase
    where TModel : BaseModel
    where TDto : BaseDto
{
    protected readonly IBasePostUseCase<TModel, TDto> _useCase;

    protected BasePostController(IBasePostUseCase<TModel, TDto> useCase)
    {
        _useCase = useCase;

    }

    [HttpPost]
    public virtual async Task<ActionResult<TDto>> Create(TDto dto)
    {

        try
        {
            var createdItem = await this._useCase.PostAsync(dto);
            return Ok(createdItem);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = e.Message });
        }

    }
}
