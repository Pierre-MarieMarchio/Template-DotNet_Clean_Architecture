using System;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;
using CleanArchitectureTemplate.Application.Modules.AuthModule.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureTemplate.API.Modules.AuthModule;

[Route("api/Auth")]
[ApiController]
public class AuthLoginController : ControllerBase
{

    private readonly ILoginUseCase _useCase;

    public AuthLoginController(ILoginUseCase useCase)
    {
        _useCase = useCase;

    }

    [HttpPost("Login")]
    public virtual async Task<ActionResult<LoginResponseDto>> Create(LoginRequestDto dto)
    {

        try
        {
            var response = await this._useCase.LoginAsync(dto);
            return Ok(response);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = e.Message });
        }

    }
}
