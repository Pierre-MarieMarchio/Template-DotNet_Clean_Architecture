using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;

namespace AppTemplate.Infrastructure.InMemory.Features.ExternalLogins;

public sealed class AcceptedExternalIdentities
{
    private readonly object _gate = new();

    private readonly Dictionary<PresentedToken, VerifiedExternalIdentity> _arranged = [];

    public void Accept(string provider, string idToken, VerifiedExternalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        lock (_gate)
        {
            _arranged[new PresentedToken(provider, idToken)] = identity;
        }
    }

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

    private readonly record struct PresentedToken(string Provider, string IdToken);
}
