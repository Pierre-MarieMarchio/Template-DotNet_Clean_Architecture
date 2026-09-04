namespace AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;

/// <param name="Token">
/// Single-use, and must never be logged. Null for
/// <see cref="EmailChangeRequestStatus.IncorrectCurrentPassword"/>, and also for a
/// <see cref="EmailChangeRequestStatus.Requested"/> outcome whose target address is already
/// registered — deliberately indistinguishable from a token that was minted, or a caller could use
/// "was anything sent?" to test which addresses exist.
/// </param>
/// <param name="UserName">Carried so the mail can address the holder without a second lookup. Set only alongside <paramref name="Token"/>.</param>
public sealed record EmailChangeRequestOutcome(EmailChangeRequestStatus Status, string? UserName = null, string? Token = null)
{
    public static EmailChangeRequestOutcome IncorrectCurrentPassword { get; } =
        new(EmailChangeRequestStatus.IncorrectCurrentPassword);

    /// <summary>The password matched, but the new address is already registered: nothing to send.</summary>
    public static EmailChangeRequestOutcome Suppressed { get; } = new(EmailChangeRequestStatus.Requested);

    public static EmailChangeRequestOutcome Issued(string userName, string token) =>
        new(EmailChangeRequestStatus.Requested, userName, token);
}
