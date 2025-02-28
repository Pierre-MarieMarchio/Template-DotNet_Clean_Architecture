using System;
using System.Security.Claims;
using CleanArchitectureTemplate.Domain.Modules.AuthModule;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Interfaces;

public interface ISignInManager
{
    public Task<SignInResult> CheckPasswordAsync(User user, string password, bool lockoutOnFailure);
    public Task<IList<Claim>> GetUserClaims(User user);
}
