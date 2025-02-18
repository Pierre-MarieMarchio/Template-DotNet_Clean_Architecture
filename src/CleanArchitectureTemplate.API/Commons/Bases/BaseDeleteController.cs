using System;
using CleanArchitectureTemplate.Application.Commons.Bases;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureTemplate.API.Commons.Bases;

[Route("api/[controller]")]
[ApiController]
public abstract class BaseDeleteController<TModel, TDto> : ControllerBase
    where TModel : BaseModel
    where TDto : BaseDto
{
    protected readonly IBaseDeleteUseCase<TModel, TDto> _useCase;

    protected BaseDeleteController(IBaseDeleteUseCase<TModel, TDto> useCase)
    {
        _useCase = useCase;
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var success = await this._useCase.DeleteAsync(id);

            if (!success)
            {
                return NotFound(new { message = $"{typeof(TModel).Name} not found" });
            }
            else
            {
                return NoContent();
            }
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = e.Message });
        }

    }
}
