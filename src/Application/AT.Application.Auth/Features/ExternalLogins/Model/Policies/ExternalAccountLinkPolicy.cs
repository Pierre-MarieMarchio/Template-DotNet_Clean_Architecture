using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.Model.Policies;

public static class ExternalAccountLinkPolicy
{
    public static ExternalAccountLinkDecision Decide(LocalAccountMatch? match) => match switch
    {
        null => ExternalAccountLinkDecision.Provision,

        { EmailConfirmed: true } => ExternalAccountLinkDecision.Link,

        _ => ExternalAccountLinkDecision.Refuse,
    };
}
