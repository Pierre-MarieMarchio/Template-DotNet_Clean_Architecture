namespace AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;

public interface ISecurityEventOriginProvider
{
    string? IpAddress { get; }

    string? UserAgent { get; }
}
