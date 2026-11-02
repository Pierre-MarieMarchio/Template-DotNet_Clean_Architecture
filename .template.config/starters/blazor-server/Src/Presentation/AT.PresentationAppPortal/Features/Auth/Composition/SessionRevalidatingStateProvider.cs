using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace AppTemplate.PresentationAppPortal.Features.Auth.Composition;

internal sealed class SessionRevalidatingStateProvider(ILoggerFactory loggerFactory, ActiveSessionProvider sessions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => ActiveSessionProvider.RevalidationInterval;

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken) =>
        authenticationState.User.Identity?.IsAuthenticated is true
            ? sessions.IsActiveAsync(authenticationState.User, cancellationToken)
            : Task.FromResult(true);
}
