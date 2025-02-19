using System;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;

public class RegisterDto
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public required string UserName { get; set; }
}
