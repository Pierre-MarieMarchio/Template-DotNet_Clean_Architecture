using System;
using System.Security.Claims;
using CleanArchitectureTemplate.Domain.Modules.AuthModule;
using CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Manager;


public class SignInManager : ISignInManager
{
    private readonly SignInManager<IdentityUser<Guid>> _signInManager;
    private readonly UserManager<IdentityUser<Guid>> _userManager;

    public SignInManager(SignInManager<IdentityUser<Guid>> signInManager, UserManager<IdentityUser<Guid>> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    public async Task<SignInResult> CheckPasswordAsync(User user, string password, bool lockoutOnFailure)
    {
        var appUser = UserToAppUser(user);
        var result = await _signInManager.CheckPasswordSignInAsync(appUser, password, lockoutOnFailure);
        return result;
    }

    public async Task<IList<Claim>> GetUserClaims(User user)
    {
        var appUser = UserToAppUser(user);
        var userClaims = await _userManager.GetClaimsAsync(appUser);
        return userClaims;
    }

    private IdentityUser<Guid> UserToAppUser(User user)
    {
        return new IdentityUser<Guid>
        {
            Id = user.Id,
            Email = user.Email,
            UserName = user.UserName,
            PasswordHash = user.PasswordHash,
        };
    }
}