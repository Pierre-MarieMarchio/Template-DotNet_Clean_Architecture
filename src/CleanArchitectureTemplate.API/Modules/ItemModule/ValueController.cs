using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Application.Modules.ItemModule.DTOs;
using CleanArchitectureTemplate.Application.Modules.ItemModule.Interfaces;
using CleanArchitectureTemplate.Domain.Modules.ItemModule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureTemplate.API.Modules.ItemModule;

[Route("api/[controller]")]
[ApiController]
public class ValueController : ControllerBase
{

    protected readonly IItemGetUseCase<Item, ItemDto> _getUseCase;

    public ValueController(IItemGetUseCase<Item, ItemDto> getUseCase)
    {
        _getUseCase = getUseCase;
    }

    [Authorize]
    [HttpGet]
    public virtual async Task<ActionResult<ItemDto>> GetAll()
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


    [HttpGet("getunauth")]
    public virtual async Task<ActionResult<ItemDto>> Getall2()
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

}






