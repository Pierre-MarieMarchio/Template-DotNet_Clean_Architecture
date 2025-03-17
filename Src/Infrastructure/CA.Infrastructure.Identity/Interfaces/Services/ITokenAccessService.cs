using System.Security.Claims;


namespace CA.Infrastructure.Identity.Interfaces.Services;

public interface ITokenAccessService
{
    public string GenerateAccessToken(IEnumerable<Claim> userClaim);

}
