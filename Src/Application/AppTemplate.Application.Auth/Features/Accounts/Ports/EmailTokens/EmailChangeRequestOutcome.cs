namespace AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;

/// <summary>How a request to move an account to a new address ended.</summary>
/// <param name="Status">Which of the ways it ended.</param>
/// <param name="Token">The single-use token for the new address; set only for <see cref="EmailChangeRequestStatus.Requested"/>.</param>
public sealed record EmailChangeRequestOutcome(EmailChangeRequestStatus Status, string? Token = null)
{
    /// <summary>The current password did not match: nothing was minted, nothing is to be sent.</summary>
    public static EmailChangeRequestOutcome IncorrectCurrentPassword { get; } =
        new(EmailChangeRequestStatus.IncorrectCurrentPassword);

    /// <summary>Nothing was minted — see <see cref="EmailChangeRequestStatus.NoSuchAccount"/>.</summary>
    public static EmailChangeRequestOutcome NoSuchAccount { get; } = new(EmailChangeRequestStatus.NoSuchAccount);

    /// <summary>The password matched, but another account already holds the new address.</summary>
    public static EmailChangeRequestOutcome AddressAlreadyRegistered { get; } =
        new(EmailChangeRequestStatus.AddressAlreadyRegistered);

    /// <summary>The password matched and there is a token to mail to the new address.</summary>
    public static EmailChangeRequestOutcome Issued(string token) =>
        new(EmailChangeRequestStatus.Requested, token);
}
