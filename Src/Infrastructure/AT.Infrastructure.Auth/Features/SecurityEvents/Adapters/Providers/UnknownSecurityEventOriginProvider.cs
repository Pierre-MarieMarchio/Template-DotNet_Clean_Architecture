namespace AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;

internal sealed class UnknownSecurityEventOriginProvider : ISecurityEventOriginProvider
{
    public string? IpAddress => null;

    public string? UserAgent => null;
}
