namespace AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;

/// <summary>
/// Where the request behind a security event came from, as the host can tell. The host supplies it
/// — the API reads the current request — because this module must not depend on one: a process that
/// only runs background work composes it too, and has no request to read.
/// <para>
/// Not an application port. The use cases never see it, the same way they never see
/// <c>IAuditActor</c>: the origin is something the log adapter attaches, not a fact a use case
/// decides. Without a host registration the module's own default answers <c>null</c> for both.
/// </para>
/// </summary>
public interface ISecurityEventOriginProvider
{
    /// <summary>The caller's address as the host resolved it, forwarded headers applied; <c>null</c> when unknown.</summary>
    string? IpAddress { get; }

    /// <summary>The request's <c>User-Agent</c> header; <c>null</c> when it sent none or there is no request.</summary>
    string? UserAgent { get; }
}
