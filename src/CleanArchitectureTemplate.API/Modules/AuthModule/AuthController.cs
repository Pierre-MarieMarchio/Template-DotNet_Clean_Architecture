using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;
using CleanArchitectureTemplate.Application.Modules.AuthModule.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureTemplate.API.Modules.AuthModule;


[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{

    private readonly ILoginUseCase _loginUseCase;
    private readonly IRegisterUseCase _registerUseCase;

    public AuthController(ILoginUseCase loginUseCase, IRegisterUseCase registerUseCase)
    {
        _loginUseCase = loginUseCase;
        _registerUseCase = registerUseCase;

    }

    [HttpPost("Login")]
    public virtual async Task<ActionResult<LoginResponseDto>> Create(LoginRequestDto dto)
    {

        try
        {
            var response = await this._loginUseCase.LoginAsync(dto);
            return Ok(response);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = e.Message });
        }

    }

    [HttpPost("Register")]
    public virtual async Task<ActionResult<UserDto>> Create(RegisterDto dto)
    {

        try
        {
            var createdItem = await this._registerUseCase.RegisterAsync(dto);
            return Ok(createdItem);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "An unexpected error occurred.", details = e.Message });
        }

    }
}