using System.Security.Claims;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

namespace AppTemplate.Api.Features.Auth.Composition;

internal sealed class CurrentSession(IHttpContextAccessor httpContextAccessor) : ICurrentSession
{
    private HttpContext? Context => httpContextAccessor.HttpContext;

    public Guid? SessionId
    {
        get
        {
            string? sid = Context?.User.FindFirstValue("sid");

            return Guid.TryParse(sid, out var session) ? session : null;
        }
    }

    public SessionClient Client
    {
        get
        {
            string? userAgent = Context?.Request.Headers.UserAgent.ToString();

            return string.IsNullOrWhiteSpace(userAgent)
                ? SessionClient.Unknown
                : new SessionClient(userAgent);
        }
    }
}

internal sealed class NoCallerCurrentSession : ICurrentSession
{
    public Guid? SessionId => throw new NotSupportedException(
        "This host has no current session: it runs with no request and no principal. A use case " +
        $"that reads {nameof(ICurrentSession)}.{nameof(SessionId)} cannot run unmodified from here.");

    public SessionClient Client => SessionClient.Unknown;
}

internal static class CurrentSessionExtensions
{
    internal static IServiceCollection AddApiCurrentSession(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICurrentSession>(static provider =>
            provider.GetRequiredService<BackgroundScope>().IsActive
                ? new NoCallerCurrentSession()
                : new CurrentSession(provider.GetRequiredService<IHttpContextAccessor>()));

        return services;
    }
}
