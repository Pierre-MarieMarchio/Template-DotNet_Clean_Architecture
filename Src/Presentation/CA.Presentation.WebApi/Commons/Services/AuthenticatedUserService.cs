using CA.Application.Commons.Interfaces.Services;
using System.Security.Claims;

namespace CA.Presentation.WebApi.Commons.Services;

public class AuthenticatedUserService(IHttpContextAccessor httpContextAccessor) : IAuthenticatedUserService
{
    public string UserId { get; } = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public string UserName { get; } = httpContextAccessor.HttpContext?.User.Identity?.Name!;
}
