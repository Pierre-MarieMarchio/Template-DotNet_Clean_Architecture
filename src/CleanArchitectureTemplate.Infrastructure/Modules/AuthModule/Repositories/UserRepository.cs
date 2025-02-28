using System;
using CleanArchitectureTemplate.Domain.Modules.AuthModule;
using CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Repositories;

public class UserRepository : IUserRepository
{
    private readonly UserManager<IdentityUser<Guid>> _userManager;

    public UserRepository(UserManager<IdentityUser<Guid>> user)
    {
        _userManager = user;
    }

    public async Task<User> GetUserbyEmailAsync(string email)
    {
        var result = await _userManager.FindByEmailAsync(email) ?? throw new UnauthorizedAccessException($"Email or pasword not Valide");
        
        return new User
        {
            Id = result.Id,
            UserName = result.UserName ?? string.Empty,
            Email = result.Email ?? string.Empty,
            PasswordHash = result.PasswordHash ?? string.Empty,
        };
    }

    public async Task<User> GetUserbyIdAsync(string id)
    {
        var result = await _userManager.FindByIdAsync(id) ?? throw new UnauthorizedAccessException($"User not valid");

        return new User
        {
            Id = result.Id,
            UserName = result.UserName ?? string.Empty,
            Email = result.Email ?? string.Empty,
            PasswordHash= result.PasswordHash ?? string.Empty,
        };
    }

    public async Task<IdentityResult> CreateUserAsync(string email, string userName, string password)
    {

        var appUser = new IdentityUser<Guid>
        {
            UserName = userName,
            Email = email,
        };

        return await _userManager.CreateAsync(appUser, password);
    }

    public async Task<IdentityResult> UpdateUserAsync(User user)
    {
        var appUser = new IdentityUser<Guid>
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
        };
        
        return await _userManager.UpdateAsync(appUser);
    }

    public async Task<IdentityResult> DeleteUserdAsync(User user)
    {
        var appUser = new IdentityUser<Guid>
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
        };

        return await _userManager.DeleteAsync(appUser);

    }
}