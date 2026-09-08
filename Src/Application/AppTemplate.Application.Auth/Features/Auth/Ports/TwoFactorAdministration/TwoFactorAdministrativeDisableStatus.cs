namespace AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorAdministration;

/// <summary>How stripping a second factor on an administrator's authority ended.</summary>
public enum TwoFactorAdministrativeDisableStatus
{
    /// <summary>Applied, whether the account had its second factor armed or not. See <c>DisableAsync</c>.</summary>
    Disabled,

    /// <summary>No account has that id.</summary>
    NoSuchAccount,

    /// <summary>The account was found but the store refused the change itself.</summary>
    Rejected,
}
