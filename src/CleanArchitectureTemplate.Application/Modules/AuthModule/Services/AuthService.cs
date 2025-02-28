using System;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;
using CleanArchitectureTemplate.Application.Modules.AuthModule.Interfaces;
using CleanArchitectureTemplate.Domain.Modules.AuthModule;
using CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Interfaces;
using CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Manager;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.Services;

public class AuthService : IAuthService
{
    private readonly ISignInManager _signInManager;
    public AuthService(ISignInManager signInManager)
    {
        _signInManager = signInManager;
    }

    public async Task<LoginResponseDto> AuthenticateUserAsync(User user, string password, bool lockoutOnFailure = false)
    {
        var result = await this._signInManager.CheckPasswordAsync(user, password, lockoutOnFailure);

        if (result.IsLockedOut)
        {
            throw new InvalidOperationException("you a  locked from login");
        }

        if (result.IsNotAllowed)
        {
            throw new InvalidOperationException("you a are not allowed");
        }
        
        if (result.RequiresTwoFactor)
        {
            throw new NotImplementedException();
        }

        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Email or password not valid");
        }

        return new LoginResponseDto
        {
            AccessToken = "accessToken",
            ExpirationTime = "expirationTime",
            RefreshToken = "refreshToken",
        };
    }
}
