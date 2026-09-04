using AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;

namespace AppTemplate.Infrastructure.InMemory.Features.Auth;

/// <summary>
/// An <see cref="IExternalIdentityVerifier"/> that talks to no identity provider.
/// <para>
/// The real adapter fetches a key set over HTTP from Google, Microsoft or Apple, which is the same
/// kind of dependency as an SMTP relay or an S3 bucket and gets the same treatment here. It is also
/// the one port a test could not otherwise reach at all: signing a token this API would accept means
/// holding a provider's private key.
/// </para>
/// <para>
/// It verifies nothing and is not meant to. What a token means is
/// <see cref="AcceptedExternalIdentities"/>'s business, arranged by the test; the checks the real
/// adapter makes are proved against real signatures in
/// <c>AppTemplate.Infrastructure.Identity.UnitTests</c>, where they belong.
/// </para>
/// </summary>
internal sealed class InMemoryExternalIdentityVerifier(AcceptedExternalIdentities accepted)
    : IExternalIdentityVerifier
{
    public Task<ExternalIdentityOutcome> VerifyAsync(
        string provider,
        string idToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(accepted.Verify(provider, idToken));
    }
}
