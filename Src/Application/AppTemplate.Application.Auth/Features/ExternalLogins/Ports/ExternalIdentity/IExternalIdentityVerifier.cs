namespace AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;

public interface IExternalIdentityVerifier
{
    Task<VerifiedExternalIdentity?> VerifyAsync(
        string provider,
        string idToken,
        CancellationToken cancellationToken = default);
}
