using CA.Application.Modules.Auth.DTOs.Requests;
using CA.Infrastructure.Identity.Entity;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace CA.Infrastructure.Identity.Interfaces.Services;

public interface IAppUserService
{
    public Task<AppUser> CheckAndGeAsync(LoginRequest loginRequest, bool lockoutOnFailur = false);
    public Task<AppUser> GetByEmailAsync(string email);
    public Task<AppUser> CreateAsync(AppUser user, string password);
    public Task<IEnumerable<Claim>> GenarateClaimsAsync(AppUser user);
    public Task<string> GenerateEmailTokenAsync(AppUser user);
    public Task<IdentityResult> ValidateEmailAsync(string email, string token);
    public AppUser MapToAppUser(RegisterRequest user);
}
