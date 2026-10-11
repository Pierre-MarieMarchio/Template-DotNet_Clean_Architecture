using System.Security.Claims;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Auth.Features.Auth.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;

namespace AppTemplate.Api.Features.Auth.Security;

/// <summary>
/// Reads the session a request is made from, and the client making it: the host owns both, for
/// the same reason <see cref="AppTemplate.Api.Common.Security.CurrentUser"/> owns the caller — only a request carries them.
/// </summary>
/// <remarks>
/// The <c>sid</c> claim is read as written: no claim-type mapping renames it, so a token this API
/// issued answers with the session it was issued in, and a token without the claim — one issued
/// before sessions were named in it, or one minted elsewhere — answers <c>null</c>. A use case that
/// would spare the caller's session spares nothing on <c>null</c>: unknown is not none.
/// </remarks>
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

    /// <summary>Absent becomes <c>null</c> rather than an empty label, so the session list shows "unknown client".</summary>
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

/// <summary>
/// The answer a background scope gives to "which session is this": none — and it refuses the
/// question rather than answering it, for the reason
/// <see cref="AppTemplate.Presentation.Core.Common.Security.NoCallerCurrentUser"/> gives. The
/// client is answered as unknown too: there is no request to read one from.
/// </summary>
internal sealed class NoCallerCurrentSession : ICurrentSession
{
    /// <summary>Always throws. There is no session a background job could be making a request from.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Guid? SessionId => throw new NotSupportedException(
        "This host has no current session: it runs with no request and no principal. A use case " +
        $"that reads {nameof(ICurrentSession)}.{nameof(SessionId)} cannot run unmodified from here.");

    public SessionClient Client => SessionClient.Unknown;
}

internal static class CurrentSessionExtensions
{
    /// <summary>
    /// Registers which session a request is made from, answering by the kind of scope it was
    /// resolved in — a request keeps its token's <c>sid</c>, a background scope gets
    /// <see cref="NoCallerCurrentSession"/>, which throws — mirroring
    /// <see cref="AppTemplate.Api.Common.Security.CurrentUserExtensions.AddApiCallerIdentity"/> next to it.
    /// </summary>
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
