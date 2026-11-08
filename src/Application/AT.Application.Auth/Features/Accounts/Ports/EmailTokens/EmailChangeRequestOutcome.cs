namespace AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;

public sealed record EmailChangeRequestOutcome(EmailChangeRequestStatus Status, string? Token = null)
{
    public static EmailChangeRequestOutcome IncorrectCurrentPassword { get; } =
        new(EmailChangeRequestStatus.IncorrectCurrentPassword);

    public static EmailChangeRequestOutcome NoSuchAccount { get; } = new(EmailChangeRequestStatus.NoSuchAccount);

    public static EmailChangeRequestOutcome AddressAlreadyRegistered { get; } =
        new(EmailChangeRequestStatus.AddressAlreadyRegistered);

    public static EmailChangeRequestOutcome Issued(string token) =>
        new(EmailChangeRequestStatus.Requested, token);
}
