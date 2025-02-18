using System;
using CleanArchitectureTemplate.Application.Commons.Bases;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitectureTemplate.API.Commons.Bases;


[Route("api/[controller]")]
[ApiController]
public abstract class BasePutController<TModel, TDto> : ControllerBase
    where TModel : BaseModel
    where TDto : BaseDto
{
    protected readonly IBasePutUseCase<TModel, TDto> _useCase;

    protected BasePutController(IBasePutUseCase<TModel, TDto> useCase)
    {
        _useCase = useCase;
    }

    [HttpPut("{id}")]
    public virtual async Task<IActionResult> Update(Guid id, TDto itemDTO)
    {

        try
        {
            var updatedItem = await this._useCase.PutAsync(id, itemDTO);
            return Ok(updatedItem);
        }
        catch (ArgumentException)
        {
            return BadRequest("The provided ID does not match the item ID.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"TodoItem with ID {id} not found.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("A concurrency conflict occurred while updating the TodoItem.");
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = e.Message });
        }
    }
}
