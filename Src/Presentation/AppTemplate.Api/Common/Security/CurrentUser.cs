using System.Security.Claims;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Presentation.Core.Common.Security;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Reads the caller from the current request's principal. The claims are read per access rather
/// than in the constructor, so resolving this service before authentication has run cannot cache
/// an absent identity for the rest of the request.
/// </summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public UserId? UserId
    {
        get
        {
            string? subject = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(subject, out var claimed)
                ? AppTemplate.Domain.Core.Common.Primitives.UserId.CreateOptional(claimed)
                : null;
        }
    }
}

internal static class CurrentUserExtensions
{
    /// <summary>
    /// Registers who is calling and whom to record, each answering by the kind of scope it was
    /// resolved in: a request keeps its principal and its audit attribution, a background scope
    /// gets <see cref="NoCallerCurrentUser"/>, which throws, and <see cref="BackgroundAuditActor"/>,
    /// which records nobody.
    /// </summary>
    /// <remarks>
    /// Also absorbs <c>AddHttpContextAccessor</c>, which is the only reason a host would have called
    /// it: nothing else in this project reaches for the ambient request. The audit actor is chosen
    /// without asking <see cref="ICurrentUser"/>, whose answer in a background scope is a refusal.
    /// </remarks>
    internal static IServiceCollection AddApiCallerIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddBackgroundScope();

        services.AddScoped<ICurrentUser>(static provider =>
            provider.GetRequiredService<BackgroundScope>().IsActive
                ? new NoCallerCurrentUser()
                : new CurrentUser(provider.GetRequiredService<IHttpContextAccessor>()));

        // The other fact about the caller the request carries, beside who they are: when the token
        // authenticating it was issued. Registered in both scopes so nothing that resolves one in
        // a background scope finds the other missing; in one, it answers null and fails closed.
        services.AddScoped<ICurrentAccessToken, CurrentAccessToken>();

        // Mandatory: no module supplies a default, so omitting it fails at composition rather than
        // stamping null over every audited row.
        services.AddScoped<IAuditActor>(static provider =>
            provider.GetRequiredService<BackgroundScope>().IsActive
                ? new BackgroundAuditActor()
                : new CurrentUserAuditActor(provider.GetRequiredService<ICurrentUser>()));

        return services;
    }
}
