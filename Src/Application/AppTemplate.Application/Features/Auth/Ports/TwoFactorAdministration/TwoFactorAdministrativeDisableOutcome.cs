namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorAdministration;

public enum TwoFactorAdministrativeDisableOutcome
{
    /// <summary>Applied, whether the account had its second factor armed or not. See <c>DisableAsync</c>.</summary>
    Disabled,

    NoSuchAccount,

    /// <summary>The account was found but the store refused the change itself.</summary>
    Rejected,
}
