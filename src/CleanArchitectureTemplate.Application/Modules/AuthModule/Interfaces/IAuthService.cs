using System;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;
using CleanArchitectureTemplate.Domain.Modules.AuthModule;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.Interfaces;

public interface IAuthService
{
    Task<LoginResponseDto> AuthenticateUserAsync(User user, string password, bool lockoutOnFailure = false);
}
