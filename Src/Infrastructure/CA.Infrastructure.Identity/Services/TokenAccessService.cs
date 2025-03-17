using CA.Infrastructure.Identity.Interfaces.Services;
using CA.Infrastructure.Identity.Settings;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CA.Infrastructure.Identity.Services;

class TokenAccessService(JwtSettings jwtSettings) : JwtService(jwtSettings), ITokenAccessService
{
    public string GenerateAccessToken(IEnumerable<Claim> userClaim)
    {
        var signingCredentials = GetSigningCredentials();
        var jwtSecurityToken = CreateJwtToken(userClaim, signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(jwtSecurityToken);
    }
}
