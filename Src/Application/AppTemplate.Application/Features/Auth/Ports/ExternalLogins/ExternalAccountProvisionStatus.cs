namespace AppTemplate.Application.Features.Auth.Ports.ExternalLogins;

public enum ExternalAccountProvisionStatus
{
    Provisioned,

    /// <summary>
    /// Nothing was created. The address or the derived user name was taken between the lookup and
    /// this call, or the store rejected the values. No message travels with it: unlike a registration,
    /// the caller submitted neither a password nor a user name, so there is nothing it could correct.
    /// </summary>
    Refused,
}
