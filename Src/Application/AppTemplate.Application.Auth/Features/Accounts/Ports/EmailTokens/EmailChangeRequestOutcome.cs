namespace AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;

/// <param name="Token">
/// Single-use, and must never be logged. Null for
/// <see cref="EmailChangeRequestStatus.IncorrectCurrentPassword"/> and
/// <see cref="EmailChangeRequestStatus.NoSuchAccount"/>, and also for a
/// <see cref="EmailChangeRequestStatus.Requested"/> outcome whose target address is already
/// registered — deliberately indistinguishable from a token that was minted, or a caller could use
/// "was anything sent?" to test which addresses exist.
/// </param>
public sealed record EmailChangeRequestOutcome(EmailChangeRequestStatus Status, string? Token = null)
{
    /// <summary>The current password did not match: nothing was minted, nothing is to be sent.</summary>
    public static EmailChangeRequestOutcome IncorrectCurrentPassword { get; } =
        new(EmailChangeRequestStatus.IncorrectCurrentPassword);

    /// <summary>Nothing was minted — see <see cref="EmailChangeRequestStatus.NoSuchAccount"/>.</summary>
    public static EmailChangeRequestOutcome NoSuchAccount { get; } = new(EmailChangeRequestStatus.NoSuchAccount);

    /// <summary>The password matched, but the new address is already registered: nothing to send.</summary>
    public static EmailChangeRequestOutcome Suppressed { get; } = new(EmailChangeRequestStatus.Requested);

    /// <summary>The password matched and there is a token to mail to the new address.</summary>
    public static EmailChangeRequestOutcome Issued(string token) =>
        new(EmailChangeRequestStatus.Requested, token);
}
