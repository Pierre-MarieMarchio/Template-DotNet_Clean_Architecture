using System;
using System.ComponentModel;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;
using CleanArchitectureTemplate.Application.Modules.AuthModule.Interfaces;
using CleanArchitectureTemplate.Domain.Modules.AuthModule;
using CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;



namespace CleanArchitectureTemplate.Application.Modules.AuthModule.Services;

public class AuthService : IAuthService
{
    private readonly ISignInManager _signInManager;
    private readonly IConfiguration _configuration;
    public AuthService(ISignInManager signInManager, IConfiguration configuration)
    {
        _signInManager = signInManager;
        _configuration = configuration;
    }

    public async Task<LoginResponseDto> AuthenticateUserAsync(User user, string password, bool lockoutOnFailure = false)
    {
        var result = await this._signInManager.CheckPasswordAsync(user, password, lockoutOnFailure);

        if (result.IsLockedOut)
        {
            throw new InvalidOperationException("locked from login");
        }

        if (result.IsNotAllowed)
        {
            throw new InvalidOperationException("not allowed");
        }

        if (result.RequiresTwoFactor)
        {
            throw new NotImplementedException();
        }

        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Email or password is incorrect");
        }

        var jwtToken = await GenerateJwtTokenAsync(user);

        return new LoginResponseDto
        {
            AccessToken = jwtToken,
            ExpirationTime = int.Parse(_configuration["JwtSettings:DurationInMinutes"]!),
            RefreshToken = "refreshToken",
        };
    }

    private async Task<string> GenerateJwtTokenAsync(User user)
    {

        var userClaims = await _signInManager.GetUserClaims(user);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("UserId", user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, user.UserName),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),

        }.Union(userClaims);

        var symetricSecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JwtSettings:SecretKey"]!));
        var signingCredentials = new SigningCredentials(symetricSecurityKey, SecurityAlgorithms.HmacSha256);

        var jwtSecurityToken = new JwtSecurityToken(
            issuer: _configuration["JwtSettings:Issuer"],
            audience: _configuration["JwtSettings:Audience"],
            claims: claims,
            expires: DateTime.Now.AddMinutes(int.Parse(_configuration["JwtSettings:DurationInMinutes"]!)),
            signingCredentials: signingCredentials
        );

        return new JwtSecurityTokenHandler().WriteToken(jwtSecurityToken);
    }
}
