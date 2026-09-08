namespace AppTemplate.Application.Auth.Features.Auth.Ports.ExternalLogins;

/// <summary>How provisioning an account for a provider identity ended.</summary>
public enum ExternalAccountProvisionStatus
{
    /// <summary>The account was created and the identity linked, as one step.</summary>
    Provisioned,

    /// <summary>
    /// Nothing was created. The address or the derived user name was taken between the lookup and
    /// this call, or the store rejected the values. No message travels with it: unlike a registration,
    /// the caller submitted neither a password nor a user name, so there is nothing it could correct.
    /// </summary>
    Refused,
}
