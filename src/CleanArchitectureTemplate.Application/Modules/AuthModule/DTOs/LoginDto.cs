using System;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;

public class LoginRequestDto
{
    public required string Email { get; set; }
    public required string Password { get; set; }
}
public class LoginResponseDto
{
    public required string AccessToken { get; set; }
    public required string ExpirationTime {get; set;}
    public required string RefreshToken { get; set; }
}