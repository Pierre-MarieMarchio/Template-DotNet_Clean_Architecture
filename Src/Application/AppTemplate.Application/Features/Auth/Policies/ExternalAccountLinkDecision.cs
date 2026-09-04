namespace AppTemplate.Application.Features.Auth.Policies;

/// <summary>What <see cref="ExternalAccountLinkPolicy"/> concluded about a first link.</summary>
public enum ExternalAccountLinkDecision
{
    /// <summary>No local account holds the address: create one and link it.</summary>
    Provision,

    /// <summary>A local account holds the address and has confirmed it: attach the provider identity to it.</summary>
    Link,

    /// <summary>A local account holds the address and never confirmed it. Nothing is linked and nothing is created.</summary>
    Refuse,
}
