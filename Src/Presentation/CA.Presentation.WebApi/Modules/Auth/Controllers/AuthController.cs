using CA.Application.Modules.Auth.DTOs.Requests;
using CA.Application.Modules.Auth.Interfaces.Managers;
using CA.Domain.Modules.Auth.Exceptions;
using CA.Presentation.WebApi.Commons.Factories;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Web;

namespace CA.Presentation.WebApi.Modules.Auth.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController(IAuthManager authManager) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {

        try
        {
            var res = await authManager.Register.ExecuteAsync(request);
            return Ok(res);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (RegistrationException ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An unexpected error occurred: " + ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        try
        {
            var res = await authManager.Login.ExecuteAsync(request);

            Response.Cookies.Append("refreshToken", res.RefreshToken!.Token, CookieFactory.CookieOptions(res.RefreshToken!.ExpirationDate));
            return Ok(res);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An unexpected error occurred: " + ex.Message });
        }
    }

    [HttpPost("Authenticate")]
    public async Task<IActionResult> Authenticate()
    {

        Request.Cookies.TryGetValue("refreshToken", out var refreshToken);

        if (refreshToken == null)
        {
            return NotFound();
        }

        var req = new AuthenticateRequest
        {
            RefreshToken = refreshToken
        };

        var res = await authManager.Authenticate.ExecuteAsync(req);

        if (!res.Success)
        {
            return StatusCode(500, new { message = "An unexpected error occurred:  " });
        }

        return Ok(res);
    }


    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string email, string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            return BadRequest("Invalide URL");

        var queryParams = HttpUtility.ParseQueryString(Request.QueryString.Value!);
        var correctedToken = queryParams["token"];

        if (string.IsNullOrEmpty(correctedToken))
            return BadRequest("Invalide URL");

        var request = new ConfirmEmailRequest
        {
            Email = email,
            Token = token
        };

        var res = await authManager.ValidateEmail.ExecuteAsync(request);

        if (!res.Success)
        {
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }

        return Ok(res);
    }

    [Authorize]
    [HttpGet("test-cookie")]
    public IActionResult TestCookie()
    {
        if (Request.Cookies.TryGetValue("refreshToken", out var refreshToken))
        {
            return Ok(new { Message = "Cookie récupéré avec succès", RefreshToken = refreshToken });
        }

        return NotFound(new { Message = "Aucun cookie trouvé" });
    }
}


