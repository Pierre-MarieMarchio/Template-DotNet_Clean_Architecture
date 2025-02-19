using System;
using CleanArchitectureTemplate.Domain.Modules.AuthModule;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Interfaces;

public interface IUserRepository
{
    public Task<User> GetUserbyEmailAsync(string email);
    public Task<User> GetUserbyIdAsync(string id);
    public Task<IdentityResult> CreateUserAsync(string email, string userName, string password);
    public Task<IdentityResult> UpdateUserAsync(User user);
    public Task<IdentityResult> DeleteUserdAsync(User user);
}
