using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CleanArchitectureTemplate.Application.Commons.Bases;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Domain.Commons.Bases;
using Swashbuckle.AspNetCore.Annotations;



namespace CleanArchitectureTemplate.API.Commons.Bases;

[Route("api/[controller]")]
[ApiController]
public abstract class BaseGetController<TModel, TDto> : ControllerBase
    where TModel : BaseModel
    where TDto : BaseDto
{

    protected readonly IBaseGetUseCase<TModel, TDto> _useCase;

    protected BaseGetController(IBaseGetUseCase<TModel, TDto> useCase)
    {
        _useCase = useCase;

    }

    [HttpGet]

    public virtual async Task<ActionResult<IEnumerable<TDto>>> GetAll()
    {

        try
        {
            var items = await this._useCase.GetAllAsync();
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
            var item = await this._useCase.GetOneAsync(id);
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
}
