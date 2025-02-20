using System;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;
using CleanArchitectureTemplate.Application.Modules.AuthModule.Interfaces;
using Microsoft.AspNetCore.Mvc;


namespace CleanArchitectureTemplate.API.Modules.AuthModule;

[Route("api/Auth")]
[ApiController]
public class AuthRegisterController : ControllerBase
{

    private readonly IRegisterUseCase _useCase;

    public AuthRegisterController(IRegisterUseCase useCase)
    {
        _useCase = useCase;

    }

    [HttpPost("Register")]
    public virtual async Task<ActionResult<UserDto>> Create(RegisterDto dto)
    {

        try
        {
            var createdItem = await this._useCase.RegisterAsync(dto);
            return Ok(createdItem);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = e.Message });
        }

    }
}
