using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;

namespace AppTemplate.PresentationApp.Common.Security;

internal static class CallerIdentityExtensions
{
    internal static IServiceCollection AddCallerIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddScoped<CallerPrincipal>();
        services.AddScoped<OperationScope>();

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ICurrentAccessToken, CurrentAccessToken>();
        services.AddScoped<IAuditActor, CurrentUserAuditActor>();

        return services;
    }
}
