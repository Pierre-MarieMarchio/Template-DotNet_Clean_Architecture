using System.Security.Claims;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.PresentationAppMcp.Common.Security;

internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public UserId? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var claimed)
            ? AppTemplate.Domain.Core.Common.Primitives.UserId.CreateOptional(claimed)
            : null;
}
