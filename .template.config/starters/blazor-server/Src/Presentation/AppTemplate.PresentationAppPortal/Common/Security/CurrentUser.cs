using System.Security.Claims;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.PresentationAppPortal.Common.Security;

internal sealed class CurrentUser(CallerPrincipal caller) : ICurrentUser
{
    public UserId? UserId =>
        Guid.TryParse(caller.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var claimed)
            ? AppTemplate.Domain.Core.Common.Primitives.UserId.CreateOptional(claimed)
            : null;
}
