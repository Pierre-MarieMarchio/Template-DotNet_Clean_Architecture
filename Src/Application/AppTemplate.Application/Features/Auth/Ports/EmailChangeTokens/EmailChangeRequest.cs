namespace AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;

/// <param name="Token">
/// Single-use, and must never be logged. Null for
/// <see cref="EmailChangeRequestOutcome.IncorrectCurrentPassword"/>, and also for a
/// <see cref="EmailChangeRequestOutcome.Requested"/> outcome whose target address is already
/// registered — deliberately indistinguishable from a token that was minted, or a caller could use
/// "was anything sent?" to test which addresses exist.
/// </param>
/// <param name="UserName">Carried so the mail can address the holder without a second lookup. Set only alongside <paramref name="Token"/>.</param>
public sealed record EmailChangeRequest(EmailChangeRequestOutcome Outcome, string? UserName = null, string? Token = null)
{
    public static EmailChangeRequest IncorrectCurrentPassword { get; } =
        new(EmailChangeRequestOutcome.IncorrectCurrentPassword);

    /// <summary>The password matched, but the new address is already registered: nothing to send.</summary>
    public static EmailChangeRequest Suppressed { get; } = new(EmailChangeRequestOutcome.Requested);

    public static EmailChangeRequest Issued(string userName, string token) =>
        new(EmailChangeRequestOutcome.Requested, userName, token);
}
