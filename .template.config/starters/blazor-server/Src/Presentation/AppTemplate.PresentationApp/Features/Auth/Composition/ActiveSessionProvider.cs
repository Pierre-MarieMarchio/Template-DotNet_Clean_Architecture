using System.Globalization;
using System.Security.Claims;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.ListSessions;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.PresentationApp.Common.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AppTemplate.PresentationApp.Features.Auth.Composition;

internal sealed class ActiveSessionProvider(IServiceScopeFactory scopes, IDateTimeProvider clock)
{
    internal const string CheckedAtKey = "session.checked_at";

    internal static readonly TimeSpan RevalidationInterval = TimeSpan.FromMinutes(5);

    public async Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (!Guid.TryParse(principal.FindFirstValue("sid"), out var session))
        {
            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CallerPrincipal>().Principal = principal;

        var listed = await scope.ServiceProvider.GetRequiredService<IListSessionsUseCase>().ExecuteAsync(cancellationToken);

        return listed.IsSuccess && listed.Value.Any(item => item.Id == session);
    }

    public async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = clock.UtcNow;

        if (context.Properties.Items.TryGetValue(CheckedAtKey, out string? checkedAt)
            && DateTimeOffset.TryParse(checkedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
            && now - last < RevalidationInterval)
        {
            return;
        }

        if (context.Principal is null || !await IsActiveAsync(context.Principal, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return;
        }

        context.Properties.Items[CheckedAtKey] = now.ToString("O", CultureInfo.InvariantCulture);
        context.ShouldRenew = true;
    }
}
