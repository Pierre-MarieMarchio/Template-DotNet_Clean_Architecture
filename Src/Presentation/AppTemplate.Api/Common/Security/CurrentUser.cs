using System.Security.Claims;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Presentation.Core.Common.Security;

namespace AppTemplate.Api.Common.Security;

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
    internal static IServiceCollection AddApiCallerIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddBackgroundScope();

        services.AddScoped<ICurrentUser>(static provider =>
            provider.GetRequiredService<BackgroundScope>().IsActive
                ? new NoCallerCurrentUser()
                : new CurrentUser(provider.GetRequiredService<IHttpContextAccessor>()));

        services.AddScoped<ICurrentAccessToken, CurrentAccessToken>();

        services.AddScoped<IAuditActor>(static provider =>
            provider.GetRequiredService<BackgroundScope>().IsActive
                ? new BackgroundAuditActor()
                : new CurrentUserAuditActor(provider.GetRequiredService<ICurrentUser>()));

        return services;
    }
}
