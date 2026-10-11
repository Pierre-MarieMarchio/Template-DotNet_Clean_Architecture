namespace AppTemplate.Infrastructure.Auth.Features.Auth.Providers;

/// <summary>
/// The module's default <see cref="ISecurityEventOriginProvider"/>: no request, so no origin. What a
/// host without requests gets, and what any host gets until it registers its own.
/// </summary>
internal sealed class UnknownSecurityEventOriginProvider : ISecurityEventOriginProvider
{
    public string? IpAddress => null;

    public string? UserAgent => null;
}
