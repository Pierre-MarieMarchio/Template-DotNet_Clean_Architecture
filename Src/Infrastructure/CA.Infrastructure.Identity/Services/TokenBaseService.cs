using CA.Infrastructure.Identity.Settings;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CA.Infrastructure.Identity.Services;

abstract class JwtService(JwtSettings jwtSettings)
{

    protected JwtSettings jwtSettings = jwtSettings;

    protected SigningCredentials GetSigningCredentials()
    {
        var symmetricSecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key));
        return new SigningCredentials(symmetricSecurityKey, SecurityAlgorithms.HmacSha256);
    }

    protected JwtSecurityToken CreateJwtToken(IEnumerable<Claim> claims, SigningCredentials signingCredentials, DateTime? date = null)
    {
        return new JwtSecurityToken(
            issuer: jwtSettings.Issuer,
            audience: jwtSettings.Audience,
            claims: claims,
            expires: date ?? DateTime.Now.AddMinutes(jwtSettings.DurationInMinutes),
            signingCredentials: signingCredentials);
    }

    protected bool IsValidJwtToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
                ValidateIssuer = !string.IsNullOrEmpty(jwtSettings.Issuer),
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = !string.IsNullOrEmpty(jwtSettings.Audience),
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            tokenHandler.ValidateToken(token, validationParameters, out _);
            return true;
        }
        catch
        {
            return false;
        }
    }
}