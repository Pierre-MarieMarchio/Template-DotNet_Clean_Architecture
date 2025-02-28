using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CleanArchitectureTemplate.Application.Commons.Bases;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using Microsoft.AspNetCore.Authorization;


namespace CleanArchitectureTemplate.API.Commons.Bases;


[Route("api/[controller]")]
[ApiController]
public abstract class BaseController<TModel, TDto, TPostDto, TPutDto> : ControllerBase
    where TModel : BaseModel
    where TDto : BaseDto
    where TPostDto : BaseDto
    where TPutDto : BaseDto

{

    protected readonly IBaseGetUseCase<TModel, TDto> _getUseCase;
    protected readonly IBasePostUseCase<TModel, TPostDto> _postUseCase;
    protected readonly IBasePutUseCase<TModel, TPutDto> _putUseCase;
    protected readonly IBaseDeleteUseCase<TModel, TDto> _deleteUseCase;

    protected BaseController(
        IBaseGetUseCase<TModel, TDto> getUseCase,
        IBasePostUseCase<TModel, TPostDto> postUseCase,
        IBasePutUseCase<TModel, TPutDto> putUseCase,
        IBaseDeleteUseCase<TModel, TDto> deleteUseCase)
    {
        _getUseCase = getUseCase;
        _postUseCase = postUseCase;
        _putUseCase = putUseCase;
        _deleteUseCase = deleteUseCase;

    }

    [Authorize]
    [HttpGet]
    public virtual async Task<ActionResult<IEnumerable<TDto>>> GetAll()
    {

        try
        {
            var items = await this._getUseCase.GetAllAsync();
            return Ok(items);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public virtual async Task<ActionResult<TDto>> GetOne(Guid id)
    {

        try
        {
            var item = await this._getUseCase.GetOneAsync(id);
            return Ok(item);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = "Item not found", details = ex.Message });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = e.Message });
        }

    }


    [HttpPost]
    public virtual async Task<ActionResult<TPostDto>> Create(TPostDto dto)
    {

        try
        {
            var createdItem = await this._postUseCase.PostAsync(dto);
            return Ok(createdItem);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = e.Message });
        }

    }

    [HttpPut("{id}")]
    public virtual async Task<IActionResult> Update(Guid id, TPutDto itemDTO)
    {

        try
        {
            var updatedItem = await this._putUseCase.PutAsync(id, itemDTO);
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
        catch (Exception e)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = e.Message });
        }
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var success = await this._deleteUseCase.DeleteAsync(id);

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


