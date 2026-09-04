using System.Security.Claims;
using AppTemplate.Application.Common.Ports;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Reads the caller from the current request's principal. The claims are read per access rather
/// than in the constructor, so resolving this service before authentication has run cannot cache
/// an absent identity for the rest of the request.
/// </summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            string? subject = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(subject, out var userId) ? userId : null;
        }
    }

}
