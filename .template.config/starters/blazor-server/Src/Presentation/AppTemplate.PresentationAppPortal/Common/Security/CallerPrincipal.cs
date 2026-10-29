using System.Security.Claims;

namespace AppTemplate.PresentationAppPortal.Common.Security;

internal sealed class CallerPrincipal(IHttpContextAccessor httpContextAccessor)
{
    private ClaimsPrincipal? _principal;

    public ClaimsPrincipal Principal
    {
        get => _principal
            ?? httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException(
                $"A use case resolved outside a request has no caller: run it through {nameof(OperationScope)}, "
                + "which gives its scope the caller of the circuit.");
        set => _principal = value;
    }
}
