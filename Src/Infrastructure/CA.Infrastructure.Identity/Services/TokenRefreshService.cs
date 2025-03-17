using CA.Application.Modules.Auth.DTOs;
using CA.Infrastructure.Identity.Entity;
using CA.Infrastructure.Identity.Interfaces.Repositories;
using CA.Infrastructure.Identity.Interfaces.Services;
using CA.Infrastructure.Identity.Settings;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CA.Infrastructure.Identity.Services;

class TokenRefreshService(IRefreshTokenRepository refreshTokenRepository, JwtSettings jwtSettings) : JwtService(jwtSettings), ITokenRefreshService
{
    public async Task<RefreshToken> GenerateRefreshTokenAsync(AppUser user, IEnumerable<Claim> claims)
    {
        var signingCredentials = GetSigningCredentials();
        var expirationDate = DateTime.Now.AddDays(7);
        var jwtSecurityToken = CreateJwtToken(claims, signingCredentials, expirationDate);


        var refreshToken = new RefreshToken
        {
            Token = new JwtSecurityTokenHandler().WriteToken(jwtSecurityToken),
            UserId = user.Id,
            User = user,
            ExpirationDate = expirationDate,
        };

        await refreshTokenRepository.AddAsync(refreshToken);
        return refreshToken;

    }

    public bool VerifyRefreshToken(string token)
    {
        return IsValidJwtToken(token);
    }

    public async Task<RefreshToken> GetRefreshTokenByUserId(Guid id)
    {
        var refreshToken = await refreshTokenRepository.GetByUserIdAsync(id) ?? throw new InvalidOperationException("error no Token for this user");
        return refreshToken;

    }
    public async Task<RefreshToken> GetRefreshTokenByTokenAsync(string token)
    {
        var refreshToken = await refreshTokenRepository.GetByTokenAsync(token) ?? throw new InvalidOperationException("error no Token for this user");
        return refreshToken;

    }

    public async Task DeleteRefreshTokenAsync(AppUser user)
    {
        var refreshToken = await refreshTokenRepository.GetByUserIdAsync(user.Id);

        if (refreshToken != null)
        {
            await refreshTokenRepository.DeleteAsync(refreshToken);
        }
    }

    public async Task<RefreshToken> UpdateRefreshTokenAsync(RefreshToken refreshToken, IEnumerable<Claim> claims)
    {

        await refreshTokenRepository.DeleteAsync(refreshToken);

        var newRefreshToken = await GenerateRefreshTokenAsync(refreshToken.User, claims);

        return newRefreshToken;
    }

    public RefreshTokenDto MapToRefreshTokenDto(RefreshToken token)
    {
        return new RefreshTokenDto
        {
            Id = token.Id,
            Token = token.Token,
            UserId = token.UserId,
            ExpirationDate = token.ExpirationDate

        };
    }

}
