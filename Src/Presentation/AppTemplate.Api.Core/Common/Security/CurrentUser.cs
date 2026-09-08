using System.Security.Claims;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.Core.Common.Security;

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
    /// Absorbs <c>AddHttpContextAccessor</c>, which is the only reason a host would have called it:
    /// nothing else in this project reaches for the ambient request.
    /// </summary>
    internal static IServiceCollection AddApiCurrentUser(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }
}
