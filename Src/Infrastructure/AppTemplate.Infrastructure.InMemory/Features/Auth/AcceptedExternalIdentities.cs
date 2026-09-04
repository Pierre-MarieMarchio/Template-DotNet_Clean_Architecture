using AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;

namespace AppTemplate.Infrastructure.InMemory.Features.Auth;

/// <summary>
/// What <see cref="InMemoryExternalIdentityVerifier"/> answers with, arranged by a test.
/// <para>
/// Public and resolvable, the same shape as <c>RecordedEmails</c> and
/// <c>RecordedReminderNotifications</c> — except that this one is arranged rather than read: a test
/// cannot obtain a real <c>id_token</c> from Google, so the double has to be told what a given token
/// means before the use case asks.
/// </para>
/// <para>
/// It answers <see cref="ExternalIdentityStatus.InvalidToken"/> for anything nobody arranged, which
/// is the same thing the real adapter does with a token it cannot verify. A test that wants
/// <see cref="ExternalIdentityStatus.UnknownProvider"/> asks for it by name through
/// <see cref="Refuse"/>, because the two are deliberately indistinguishable to the caller and a
/// double that guessed between them would be deciding the policy under test.
/// </para>
/// </summary>
public sealed class AcceptedExternalIdentities
{
    private readonly object _gate = new();

    private readonly Dictionary<PresentedToken, ExternalIdentityOutcome> _arranged = [];

    /// <summary>Makes one token, presented for one provider, verify as this identity.</summary>
    public void Accept(string provider, string idToken, VerifiedExternalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Arrange(provider, idToken, ExternalIdentityOutcome.Verified(identity));
    }

    /// <summary>Makes one token refuse with a named status, so a test can reach every branch.</summary>
    public void Refuse(string provider, string idToken, ExternalIdentityStatus status)
    {
        if (status is ExternalIdentityStatus.Verified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A refusal cannot be Verified; use Accept and supply the identity it verifies as.");
        }

        Arrange(provider, idToken, ExternalIdentityOutcome.Refused(status));
    }

    /// <summary>Forgets every arrangement, so one test's tokens cannot verify in the next.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _arranged.Clear();
        }
    }

    internal ExternalIdentityOutcome Verify(string provider, string idToken)
    {
        lock (_gate)
        {
            return _arranged.TryGetValue(new PresentedToken(provider, idToken), out var outcome)
                ? outcome
                : ExternalIdentityOutcome.Refused(ExternalIdentityStatus.InvalidToken);
        }
    }

    private void Arrange(string provider, string idToken, ExternalIdentityOutcome outcome)
    {
        lock (_gate)
        {
            _arranged[new PresentedToken(provider, idToken)] = outcome;
        }
    }

    /// <summary>
    /// The pair, because a token is only meaningful for the provider it was presented for — which is
    /// the property the real adapter enforces by checking the issuer, and the one a double that
    /// keyed on the token alone would quietly stop covering.
    /// </summary>
    private readonly record struct PresentedToken(string Provider, string IdToken);
}
