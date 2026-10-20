using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;

namespace AppTemplate.Infrastructure.InMemory.Features.ExternalLogins;

internal sealed class InMemoryExternalIdentityVerifier(AcceptedExternalIdentities accepted)
    : IExternalIdentityVerifier
{
    public Task<VerifiedExternalIdentity?> VerifyAsync(
        string provider,
        string idToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(accepted.Verify(provider, idToken));
    }
}
