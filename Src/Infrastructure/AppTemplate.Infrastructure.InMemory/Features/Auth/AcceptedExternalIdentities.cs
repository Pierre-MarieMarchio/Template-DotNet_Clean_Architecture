using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalIdentity;

namespace AppTemplate.Infrastructure.InMemory.Features.Auth;

/// <summary>
/// What <see cref="InMemoryExternalIdentityVerifier"/> answers with, arranged by a test.
/// <para>
/// Public and resolvable, the same shape as <c>RecordedEmails</c> — except that this one is arranged
/// rather than read: a test cannot obtain a real <c>id_token</c> from Google, so the double has to be
/// told what a given token means before the use case asks.
/// </para>
/// <para>
/// It refuses anything nobody arranged, which is what the real adapter does with a token it cannot
/// verify and with a provider it does not know: the caller cannot tell those apart either.
/// </para>
/// </summary>
public sealed class AcceptedExternalIdentities
{
    private readonly object _gate = new();

    private readonly Dictionary<PresentedToken, VerifiedExternalIdentity> _arranged = [];

    /// <summary>Makes one token, presented for one provider, verify as this identity.</summary>
    public void Accept(string provider, string idToken, VerifiedExternalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        lock (_gate)
        {
            _arranged[new PresentedToken(provider, idToken)] = identity;
        }
    }

    /// <summary>Forgets every arrangement, so one test's tokens cannot verify in the next.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _arranged.Clear();
        }
    }

    internal VerifiedExternalIdentity? Verify(string provider, string idToken)
    {
        lock (_gate)
        {
            return _arranged.GetValueOrDefault(new PresentedToken(provider, idToken));
        }
    }

    /// <summary>
    /// The pair, because a token is only meaningful for the provider it was presented for — which is
    /// the property the real adapter enforces by checking the issuer, and the one a double that
    /// keyed on the token alone would quietly stop covering.
    /// </summary>
    private readonly record struct PresentedToken(string Provider, string IdToken);
}
