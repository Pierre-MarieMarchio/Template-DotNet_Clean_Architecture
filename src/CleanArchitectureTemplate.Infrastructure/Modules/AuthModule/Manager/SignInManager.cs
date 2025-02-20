using System;
using CleanArchitectureTemplate.Domain.Modules.AuthModule;
using CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Manager;


public class SignInManager : ISignInManager
{
    private readonly SignInManager<IdentityUser<Guid>> _signInManager;

    public SignInManager(SignInManager<IdentityUser<Guid>> signInManager)
    {
        _signInManager = signInManager;
    }

    public async Task<SignInResult> CheckPasswordAsync(User user, string password, bool lockoutOnFailure)
    {
        var appUser = new IdentityUser<Guid>
        {
            Id = user.Id,
            Email = user.Email,
            UserName = user.UserName,
            PasswordHash = user.PasswordHash,
        };

        var result = await _signInManager.CheckPasswordSignInAsync(appUser, password, lockoutOnFailure);
        return result;
    }
}